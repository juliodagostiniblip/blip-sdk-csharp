using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lime.Messaging.Contents;
using Lime.Messaging.Resources.Checkout;
using Lime.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Take.Blip.Builder.Actions.Checkout.Settings;
using Take.Blip.Client;
using Take.Blip.Client.Extensions.Builder.Checkout;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Represents an action that creates a checkout link and sends it to a customer using the specified messaging
    /// context and settings.
    /// </summary>
    /// <remarks>Use this action to initiate a checkout process by generating a checkout link based on the
    /// provided customer and product information, and delivering it through the configured messaging channel. This
    /// class is typically used in automated workflows where a checkout experience needs to be triggered and
    /// communicated to the end user.</remarks>
    public class CheckoutAction : ActionBase<CheckoutSettings>
    {
        private const string PENDING_CHECKOUT_VARIABLE = "checkoutPending";
        private readonly ICheckoutExtension _checkoutExtension;
        private readonly ILogger _logger;
        private readonly CheckoutMessageSender _messageSender;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckoutAction"/> class.
        /// </summary>
        /// <param name="checkoutExtension">The checkout extension used to create checkout links.</param>
        /// <param name="sender">The sender used to send messages.</param>
        /// <param name="logger">The logger used to log information.</param>
        public CheckoutAction(ICheckoutExtension checkoutExtension, ISender sender, ILogger logger)
            : base("Payments")
        {
            _checkoutExtension = checkoutExtension;
            _logger = logger;
            _messageSender = new CheckoutMessageSender(sender);
        }

        /// <summary>
        /// Executes the checkout process asynchronously using the specified context and settings.
        /// </summary>
        /// <param name="context">The execution context that provides access to workflow data and services.</param>
        /// <param name="settings">The settings containing customer, product, and merchant information required to create the checkout.</param>
        /// <param name="cancellationToken">A token that can be used to request cancellation of the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public override async Task ExecuteAsync(IContext context, CheckoutSettings settings, CancellationToken cancellationToken)
        {
            if (settings.PaymentMethods != null)
            {
                settings.Validate();
                var methods = settings.GetPaymentMethods();
                if (methods.Length == 1)
                {
                    await ExecuteAsync(context, methods[0], cancellationToken);
                    return;
                }

                if (!MetaOrderComponentFactory.ShouldSend(context, settings))
                    throw new InvalidOperationException("Multiple payment methods require a WhatsApp order details component.");

                await context.DeleteVariableAsync(PENDING_CHECKOUT_VARIABLE, cancellationToken);
                var documents = new Document[methods.Length];
                for (var index = 0; index < methods.Length; index++)
                {
                    var method = methods[index];
                    if (method.Provider == CheckoutSettings.PROVIDER_CHECKOUT)
                        documents[index] = await _checkoutExtension.CreateCheckOutLinkAsync(
                            method.Merchant, BuildCheckoutDocument(method), cancellationToken);
                }

                await _messageSender.SendCheckoutLinkAsync(context, settings, null, null, cancellationToken, documents);
                return;
            }

            if (settings.ProcessSavedCardResponse)
            {
                await ProcessSavedCardResponseAsync(context, settings, cancellationToken);
                return;
            }

            await context.DeleteVariableAsync(PENDING_CHECKOUT_VARIABLE, cancellationToken);
            if (settings.Provider == CheckoutSettings.PROVIDER_OWN
                && settings.PaymentMethod != CheckoutSettings.PAYMENT_METHOD_LINK
                && !MetaOrderComponentFactory.ShouldSend(context, settings))
            {
                throw new InvalidOperationException("Multiple payment methods and own-provider Pix or bank slip payments require a WhatsApp order details component.");
            }

            Document checkoutLinkDocument = null;
            string checkoutLink = null;
            if (settings.Provider == CheckoutSettings.PROVIDER_CHECKOUT)
            {
                var checkout = BuildCheckoutDocument(settings);
                checkoutLinkDocument = await _checkoutExtension.CreateCheckOutLinkAsync(
                    settings.Merchant,
                    checkout,
                    cancellationToken
                );
                checkoutLink = CheckoutLinkResolver.GetCheckoutLink(checkoutLinkDocument);

                if (settings.PaymentMethod == CheckoutSettings.PAYMENT_METHOD_LINK)
                {
                    var purchaseId = CheckoutLinkResolver.TryGetDictionaryValueAsString(
                        checkoutLinkDocument as DictionaryDocument<string, object>,
                        "checkoutId"
                    );
                    if (!string.IsNullOrWhiteSpace(purchaseId))
                    {
                        var card = await GetCustomerLastCardAsync(settings, purchaseId, cancellationToken);
                        if (
                            card?.Success == true
                            && card.HasCard
                            && !string.IsNullOrWhiteSpace(card.Id)
                            && card.Id != Guid.Empty.ToString()
                        )
                        {
                            var pending = new PendingCheckout
                            {
                                Settings = settings,
                                PurchaseId = purchaseId,
                                CardId = card.Id,
                                CardLastFourDigits = card.LastFourDigits,
                                ChoiceId = Guid.NewGuid().ToString("N"),
                                CheckoutLink = checkoutLink,
                                ReferenceId = CheckoutLinkResolver.TryGetDictionaryValueAsString(
                                    checkoutLinkDocument as DictionaryDocument<string, object>,
                                    "checkoutId"
                                )
                            };

                            await context.SetVariableAsync(
                                PENDING_CHECKOUT_VARIABLE,
                                JsonConvert.SerializeObject(pending),
                                cancellationToken,
                                TimeSpan.FromMinutes(30)
                            );
                            await _messageSender.SendSavedCardOptionsAsync(
                                context,
                                pending,
                                card.LastFourDigits,
                                cancellationToken
                            );
                            return;
                        }
                    }
                }
            }
            else if (settings.PaymentMethod == CheckoutSettings.PAYMENT_METHOD_LINK)
            {
                checkoutLink = settings.PaymentLink;
            }

            await _messageSender.SendCheckoutLinkAsync(context, settings, checkoutLinkDocument, checkoutLink, cancellationToken);
        }

        private static PaymentCheckoutRequest BuildCheckoutDocument(CheckoutSettings settings)
        {
            var checkout = new PaymentCheckoutRequest
            {
                Customer = new CustomerCheckout
                {
                    Identity = settings.Customer.Identity,
                    Phone = settings.Customer.Phone,
                    Name = settings.Customer.Name,
                    Surname = settings.Customer.Surname,
                    Email = settings.Customer.Email,
                    Document = settings.Customer.Document,
                    DocumentType = settings.Customer.DocumentType
                },
                Products = new List<ProductCheckout>(),
                Channel = settings.Channel,
                Currency = settings.Currency,
                Source = settings.Source,
                BotShortName = settings.BotShortName,
                MerchantId = settings.Merchant
            };

            foreach (var product in settings.Products)
            {
                checkout.Products.Add(new ProductCheckout
                {
                    Description = product.Description,
                    Price = product.Price,
                    Quantity = product.Quantity,
                });
            }

            return checkout;
        }

        private async Task<CustomerLastCardResult> GetCustomerLastCardAsync(CheckoutSettings settings, string purchaseId, CancellationToken cancellationToken)
        {
            try
            {
                return await _checkoutExtension.GetCustomerLastCardAsync(new CustomerLastCardRequest
                {
                    BotShortName = settings.BotShortName,
                    PurchaseId = purchaseId
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "Could not retrieve the saved card for checkout");
                return null;
            }
        }

        private async Task ProcessSavedCardResponseAsync(IContext context, CheckoutSettings settings, CancellationToken cancellationToken)
        {
            var pendingValue = await context.GetContextVariableAsync(PENDING_CHECKOUT_VARIABLE, cancellationToken);
            if (string.IsNullOrWhiteSpace(pendingValue))
            {
                return;
            }

            var pending = JsonConvert.DeserializeObject<PendingCheckout>(pendingValue);
            var message = context.Input.Message;
            var response = message.Metadata != null
                && message.Metadata.TryGetValue("#wa.interactive.button_reply.id", out var buttonId)
                && !string.IsNullOrWhiteSpace(buttonId)
                    ? buttonId
                    : TryGetSavedCardResponse(message.Content);
            var useSavedCardLegacy = $"checkout:{pending.ChoiceId}:saved-card";
            var useOtherLegacy = $"checkout:{pending.ChoiceId}:other";
            var useSavedCardInteractive = $"PAY_ONE_CLICK_{pending.CardId}";
            const string useOtherInteractive = "ACCESS_PAYMENT_LINK";
            var useSavedCard = response == useSavedCardLegacy
                || response == useSavedCardInteractive
                || (!string.IsNullOrWhiteSpace(pending.CardLastFourDigits)
                    && response == CheckoutMessageSender.GetSavedCardTitle(pending.CardLastFourDigits));

            if (!useSavedCard
                && response != useOtherLegacy
                && response != useOtherInteractive
                && response != CheckoutMessageSender.OTHER_PAYMENT_METHOD_TITLE)
            {
                return;
            }

            await context.DeleteVariableAsync(PENDING_CHECKOUT_VARIABLE, cancellationToken);
            if (useSavedCard)
            {
                await PayWithSavedCardAsync(pending, cancellationToken);
                return;
            }

            var checkoutDocument = new JsonDocument { ["checkoutId"] = pending.ReferenceId ?? pending.PurchaseId };
            await _messageSender.SendCheckoutLinkAsync(context, pending.Settings, checkoutDocument, pending.CheckoutLink, cancellationToken);
        }

        private static string TryGetSavedCardResponse(Document content)
        {
            if (content is PlainText plainText)
            {
                return plainText.Text;
            }

            if (content is Reply reply)
            {
                return TryGetSavedCardResponse(reply.Replied?.Value);
            }

            if (content is JsonDocument document)
            {
                var response = JObject.FromObject(document);
                var id = response.SelectToken("interactive.button_reply.id")
                    ?? response.SelectToken("button_reply.id")
                    ?? response["id"];
                return id?.Type == JTokenType.String ? id.Value<string>() : null;
            }

            return null;
        }

        private async Task PayWithSavedCardAsync(PendingCheckout pending, CancellationToken cancellationToken)
        {
            var result = await _checkoutExtension.PayWithSavedCardAsync(new SavedCardPaymentRequest
            {
                BotShortName = pending.Settings.BotShortName,
                PurchaseId = pending.PurchaseId,
                CardId = pending.CardId,
                Installments = 1
            }, cancellationToken);

            var resultDocument = result as DictionaryDocument<string, object>;
            var errorMessage = CheckoutLinkResolver.TryGetDictionaryValueAsString(resultDocument, "errorMessage");
            if (result == null || !string.IsNullOrWhiteSpace(errorMessage)
                || string.Equals(CheckoutLinkResolver.TryGetDictionaryValueAsString(resultDocument, "success"), "false", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Saved card payment could not be processed.");
            }
        }
    }
}
