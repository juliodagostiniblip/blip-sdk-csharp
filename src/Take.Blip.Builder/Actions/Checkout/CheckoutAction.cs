using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lime.Messaging.Contents;
using Lime.Protocol;
using Newtonsoft.Json;
using Serilog;
using Take.Blip.Builder.Actions.Checkout.Settings;
using Take.Blip.Client;
using Take.Blip.Client.Extensions.Builder.Checkout;
using Take.Blip.Client.Extensions.Builder.Checkout.Documents;

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
        private readonly ICheckoutExtension _checkoutExtension;
        private readonly ILogger _logger;
        private readonly CheckoutMessageSender _messageSender;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckoutAction"/> class.
        /// </summary>
        /// <param name="checkoutExtension">The checkout extension used to create checkout links.</param>
        /// <param name="sender">The sender used to send messages.</param>
        /// <param name="logger">The logger used to log information.</param>
        public CheckoutAction(ICheckoutExtension checkoutExtension, ISender sender, ILogger logger) : base(nameof(Checkout))
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
            if (settings.ProcessSavedCardResponse)
            {
                await ProcessSavedCardResponseAsync(context, settings, cancellationToken);
                return;
            }

            await context.DeleteVariableAsync(settings.PendingCheckoutVariable, cancellationToken);

            var checkout = BuildCheckoutDocument(settings);

            var checkoutLinkDocument = await _checkoutExtension.CreateCheckOutLinkAsync(settings.Merchant, checkout, cancellationToken);
            var checkoutLink = CheckoutLinkResolver.GetCheckoutLink(checkoutLinkDocument);

            var purchaseId = CheckoutLinkResolver.TryGetDictionaryValueAsString(checkoutLinkDocument as DictionaryDocument<string, object>, "checkoutId");
            if (!string.IsNullOrWhiteSpace(purchaseId))
            {
                var card = await GetCustomerLastCardAsync(settings, purchaseId, cancellationToken);
                if (card?.Success == true && card.HasCard && !string.IsNullOrWhiteSpace(card.Id)
                    && card.Id != Guid.Empty.ToString())
                {
                    var pending = new PendingCheckout
                    {
                        Settings = settings,
                        PurchaseId = purchaseId,
                        CardId = card.Id,
                        ChoiceId = Guid.NewGuid().ToString("N"),
                        CheckoutLink = checkoutLink,
                        ReferenceId = CheckoutLinkResolver.TryGetDictionaryValueAsString(checkoutLinkDocument as DictionaryDocument<string, object>, "checkoutId")
                    };

                    await context.SetVariableAsync(settings.PendingCheckoutVariable, JsonConvert.SerializeObject(pending),
                        cancellationToken, TimeSpan.FromMinutes(30));
                    await _messageSender.SendSavedCardOptionsAsync(context, pending, card.LastFourDigits, cancellationToken);
                    return;
                }
            }

            await _messageSender.SendCheckoutLinkAsync(context, settings, checkoutLinkDocument, checkoutLink, cancellationToken);
        }

        private static CheckoutDocument BuildCheckoutDocument(CheckoutSettings settings)
        {
            var checkout = new CheckoutDocument
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
            var pendingValue = await context.GetContextVariableAsync(settings.PendingCheckoutVariable, cancellationToken);
            if (string.IsNullOrWhiteSpace(pendingValue))
            {
                return;
            }

            var pending = JsonConvert.DeserializeObject<PendingCheckout>(pendingValue);
            var content = context.Input.Message.Content;
            var response = (content as PlainText)?.Text ?? ((content as Reply)?.Replied?.Value as PlainText)?.Text;
            if (response != $"checkout:{pending.ChoiceId}:saved-card" && response != $"checkout:{pending.ChoiceId}:other")
            {
                return;
            }

            await context.DeleteVariableAsync(settings.PendingCheckoutVariable, cancellationToken);
            if (response == $"checkout:{pending.ChoiceId}:saved-card")
            {
                await PayWithSavedCardAsync(pending, cancellationToken);
                return;
            }

            var checkoutDocument = new JsonDocument { ["checkoutId"] = pending.ReferenceId ?? pending.PurchaseId };
            await _messageSender.SendCheckoutLinkAsync(context, pending.Settings, checkoutDocument, pending.CheckoutLink, cancellationToken);
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
