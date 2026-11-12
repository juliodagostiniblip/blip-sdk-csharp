using System;
using System.Linq;
using Lime.Protocol;
using Take.Blip.Builder.Actions.Checkout.Settings;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Builds the Meta (WhatsApp) order details component for a checkout.
    /// </summary>
    internal static class MetaOrderComponentFactory
    {
        private const int MONEY_OFFSET = 100;

        public static bool ShouldSend(IContext context, CheckoutSettings settings)
        {
            if (context?.Input?.Message?.From?.Domain?.Equals("wa.gw.msging.net", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(settings?.Channel)
                   && settings.Channel.IndexOf("whatsapp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Document Create(CheckoutSettings settings, Document checkoutLinkDocument, string checkoutLink)
        {
            var referenceId = CheckoutLinkResolver.TryGetDictionaryValueAsString(checkoutLinkDocument as DictionaryDocument<string, object>, "checkoutId")
                              ?? Guid.NewGuid().ToString("N").Substring(0, 8);

            var itemDocuments = settings.Products
                .Select((p, i) => (object)new JsonDocument
                {
                    ["retailer_id"] = i.ToString(),
                    ["name"] = p.Description,
                    ["amount"] = CreateMoneyDocument(p.Price),
                    ["quantity"] = p.Quantity
                })
                .ToArray();

            var subtotal = settings.Products.Sum(p => p.Price * p.Quantity);

            return new JsonDocument
            {
                ["recipient_type"] = "individual",
                ["type"] = "interactive",
                ["interactive"] = new JsonDocument
                {
                    ["type"] = "order_details",
                    ["body"] = new JsonDocument
                    {
                        ["text"] = settings.Message
                    },
                    ["action"] = new JsonDocument
                    {
                        ["name"] = "review_and_pay",
                        ["parameters"] = new JsonDocument
                        {
                            ["reference_id"] = referenceId,
                            ["type"] = "digital-goods",
                            ["payment_type"] = "br",
                            ["payment_settings"] = new object[]
                            {
                                new JsonDocument
                                {
                                    ["type"] = "payment_link",
                                    ["payment_link"] = new JsonDocument
                                    {
                                        ["uri"] = checkoutLink
                                    }
                                }
                            },
                            ["currency"] = string.IsNullOrWhiteSpace(settings.Currency) ? "BRL" : settings.Currency,
                            ["total_amount"] = CreateMoneyDocument(subtotal),
                            ["order"] = new JsonDocument
                            {
                                ["status"] = "pending",
                                ["tax"] = new JsonDocument
                                {
                                    ["value"] = 0,
                                    ["offset"] = MONEY_OFFSET
                                },
                                ["items"] = itemDocuments,
                                ["subtotal"] = CreateMoneyDocument(subtotal)
                            }
                        }
                    }
                }
            };
        }

        private static JsonDocument CreateMoneyDocument(decimal amount)
        {
            return new JsonDocument
            {
                ["value"] = (int)Math.Round(amount * MONEY_OFFSET, MidpointRounding.AwayFromZero),
                ["offset"] = MONEY_OFFSET
            };
        }
    }
}
