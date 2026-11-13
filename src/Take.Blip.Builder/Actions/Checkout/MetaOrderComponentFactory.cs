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

        public static Document Create(CheckoutSettings settings, Document checkoutLinkDocument, string checkoutLink,
            Document[] paymentDocuments = null)
        {
            var methods = settings.GetPaymentMethods();
            checkoutLinkDocument = paymentDocuments?.FirstOrDefault(document => document != null) ?? checkoutLinkDocument;
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

            var component = new JsonDocument
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
                            ["type"] = settings.Type,
                            ["payment_type"] = "br",
                            ["payment_settings"] = methods
                                .Select((method, index) => (object)CreatePaymentSetting(method,
                                    paymentDocuments == null ? checkoutLinkDocument : paymentDocuments[index],
                                    paymentDocuments == null ? checkoutLink
                                        : method.Provider == CheckoutSettings.PROVIDER_CHECKOUT
                                            && method.PaymentMethod == CheckoutSettings.PAYMENT_METHOD_LINK
                                                ? CheckoutLinkResolver.GetCheckoutLink(paymentDocuments[index]) : null,
                                    method.PaymentMethod))
                                .ToArray(),
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

            var interactive = (JsonDocument)component["interactive"];
            if (!string.IsNullOrWhiteSpace(settings.Header))
            {
                interactive["header"] = new JsonDocument
                {
                    ["type"] = "text",
                    ["text"] = settings.Header
                };
            }

            if (!string.IsNullOrWhiteSpace(settings.Footer))
            {
                interactive["footer"] = new JsonDocument
                {
                    ["text"] = settings.Footer
                };
            }

            return component;
        }

        private static JsonDocument CreatePaymentSetting(
            CheckoutSettings settings,
            Document checkoutLinkDocument,
            string checkoutLink,
            string paymentMethod
        )
        {
            if (paymentMethod == CheckoutSettings.PAYMENT_METHOD_LINK)
            {
                var paymentLink = settings.Provider == CheckoutSettings.PROVIDER_OWN
                    ? settings.PaymentLink
                    : checkoutLink;
                return new JsonDocument
                {
                    ["type"] = "payment_link",
                    ["payment_link"] = new JsonDocument { ["uri"] = paymentLink }
                };
            }

            if (paymentMethod == CheckoutSettings.PAYMENT_METHOD_PIX)
            {
                var isOwnProvider = settings.Provider == CheckoutSettings.PROVIDER_OWN;
                var pixCode = isOwnProvider
                    ? settings.PixCode
                    : CheckoutLinkResolver.TryGetNestedDictionaryValueAsString(
                        checkoutLinkDocument as DictionaryDocument<string, object>,
                        "pix",
                        "qrCodeText"
                    );
                var businessName = isOwnProvider
                    ? settings.BusinessName
                    : CheckoutLinkResolver.TryGetNestedDictionaryValueAsString(
                        checkoutLinkDocument as DictionaryDocument<string, object>,
                        "pix",
                        "merchantName"
                    );
                var pixKey = isOwnProvider
                    ? settings.PixKey
                    : CheckoutLinkResolver.TryGetNestedDictionaryValueAsString(
                        checkoutLinkDocument as DictionaryDocument<string, object>,
                        "pix",
                        "pixKey"
                    );
                var pixKeyType = isOwnProvider
                    ? MapPixKeyType(settings.PixKeyType)
                    : "CNPJ";

                if (string.IsNullOrWhiteSpace(pixCode))
                {
                    throw new InvalidOperationException("Checkout did not return a Pix copy-and-paste code.");
                }

                return new JsonDocument
                {
                    ["type"] = "pix_dynamic_code",
                    ["pix_dynamic_code"] = new JsonDocument
                    {
                        ["code"] = pixCode,
                        ["merchant_name"] = businessName ?? string.Empty,
                        ["key"] = pixKey ?? string.Empty,
                        ["key_type"] = pixKeyType
                    }
                };
            }

            if (paymentMethod == CheckoutSettings.PAYMENT_METHOD_BANK_SLIP)
            {
                var digitableLine = new string(settings.BankSlipCode.Where(char.IsDigit).ToArray());
                return new JsonDocument
                {
                    ["type"] = "boleto",
                    ["boleto"] = new JsonDocument { ["digitable_line"] = digitableLine }
                };
            }

            throw new InvalidOperationException("Unsupported payment method.");
        }

        private static string MapPixKeyType(string pixKeyType)
        {
            switch (pixKeyType)
            {
                case CheckoutSettings.PIX_KEY_CPF:
                    return "CPF";
                case CheckoutSettings.PIX_KEY_CNPJ:
                    return "CNPJ";
                case CheckoutSettings.PIX_KEY_EMAIL:
                    return "EMAIL";
                case CheckoutSettings.PIX_KEY_PHONE:
                    return "PHONE";
                case CheckoutSettings.PIX_KEY_RANDOM:
                    return "RANDOM_KEY";
                default:
                    throw new InvalidOperationException("Unsupported Pix key type.");
            }
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
