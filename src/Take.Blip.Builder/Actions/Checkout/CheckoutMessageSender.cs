using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol;
using Take.Blip.Builder.Actions.Checkout.Settings;
using Take.Blip.Client;
using Lime.Messaging.Contents;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Responsible for building and sending the checkout related messages to the customer.
    /// </summary>
    internal class CheckoutMessageSender
    {
        internal const string OTHER_PAYMENT_METHOD_TITLE = "Outra forma";
        private readonly ISender _sender;

        public CheckoutMessageSender(ISender sender)
        {
            _sender = sender;
        }

        internal static string GetSavedCardTitle(string lastFourDigits) => $"Cartão final {lastFourDigits}";

        public async Task SendCheckoutLinkAsync(IContext context, CheckoutSettings settings, Document checkoutLinkDocument, string checkoutLink, CancellationToken cancellationToken,
            Document[] paymentDocuments = null)
        {
            var content = MetaOrderComponentFactory.ShouldSend(context, settings)
                    ? MetaOrderComponentFactory.Create(settings, checkoutLinkDocument, checkoutLink, paymentDocuments)
                    : (Document)new WebLink
                    {
                        Text = settings.Message,
                        Uri = new Uri(checkoutLink)
                    };

            await SendMessageAsync(context, content, cancellationToken);
        }

        public Task SendSavedCardOptionsAsync(IContext context, PendingCheckout pending, string lastFourDigits, CancellationToken cancellationToken)
        {
            var content = new JsonDocument
            {
                ["type"] = "interactive",
                ["interactive"] = new JsonDocument
                {
                    ["type"] = "button",
                    ["body"] = new JsonDocument
                    {
                        ["text"] = BuildSavedCardBodyText(pending)
                    },
                    ["action"] = new JsonDocument
                    {
                        ["buttons"] = new object[]
                        {
                            new JsonDocument
                            {
                                ["type"] = "reply",
                                ["reply"] = new JsonDocument
                                {
                                    ["id"] = $"PAY_ONE_CLICK_{pending.CardId}",
                                    ["title"] = GetSavedCardTitle(lastFourDigits)
                                }
                            },
                            new JsonDocument
                            {
                                ["type"] = "reply",
                                ["reply"] = new JsonDocument
                                {
                                    ["id"] = "ACCESS_PAYMENT_LINK",
                                    ["title"] = OTHER_PAYMENT_METHOD_TITLE
                                }
                            }
                        }
                    }
                }
            };

            return SendMessageAsync(context, content, cancellationToken);
        }

        private static string BuildSavedCardBodyText(PendingCheckout pending)
        {
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            var products = pending?.Settings?.Products ?? Array.Empty<ProductSettings>();
            var textBuilder = new StringBuilder();

            textBuilder.AppendLine("🛒 Resumo do seu pedido");
            textBuilder.AppendLine();

            foreach (var product in products)
            {
                var subtotal = product.Price * product.Quantity;
                textBuilder.AppendLine($"*{product.Description}*");
                textBuilder.AppendLine($"{product.Quantity} x R$ {product.Price.ToString("N2", culture)} = R$ {subtotal.ToString("N2", culture)}");
                textBuilder.AppendLine();
            }

            var total = products.Sum(product => product.Price * product.Quantity);
            textBuilder.AppendLine("-------------------------------");
            textBuilder.AppendLine($"*Valor total: R$ {total.ToString("N2", culture)}*");
            textBuilder.AppendLine("-------------------------------");
            textBuilder.AppendLine();
            textBuilder.AppendLine("Como deseja pagar?");
            textBuilder.AppendLine();

            return textBuilder.ToString();
        }

        private async Task SendMessageAsync(IContext context, Document content, CancellationToken cancellationToken)
        {
            var message = new Message
            {
                Id = EnvelopeId.NewId(),
                To = context.Input.Message.From,
                Content = content
            };

            if (context.Input.Message.From.Domain.Equals("tunnel.msging.net"))
            {
                message.Metadata ??= new Dictionary<string, string>();

                if (context.Input.Message.Metadata?.TryGetValue("#tunnel.owner", out string owner) == true)
                {
                    message.Metadata.Add("#tunnel.owner", owner);
                }

                if (context.Input.Message.Metadata?.TryGetValue("#tunnel.originator", out string originator) == true)
                {
                    message.Metadata.Add("#tunnel.originator", originator);
                }
            }

            await _sender.SendMessageAsync(message, cancellationToken);
        }
    }
}
