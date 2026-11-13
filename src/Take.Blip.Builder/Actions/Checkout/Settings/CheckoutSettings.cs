using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Take.Blip.Builder.Models;

namespace Take.Blip.Builder.Actions.Checkout.Settings
{
    public class CheckoutSettings : PaymentMethodSettings, IValidable
    {
        public const string DIGITAL_GOODS = "digital-goods";
        public const string PHYSICAL_GOODS = "physical-goods";
        public const string PAYMENT_METHOD_LINK = "paymentLink";
        public const string PAYMENT_METHOD_PIX = "pix";
        public const string PAYMENT_METHOD_BANK_SLIP = "bankSlip";
        public const string PROVIDER_CHECKOUT = "checkout";
        public const string PROVIDER_OWN = "ownProvider";
        public const string PIX_KEY_CPF = "cpf";
        public const string PIX_KEY_CNPJ = "cnpj";
        public const string PIX_KEY_EMAIL = "email";
        public const string PIX_KEY_PHONE = "phone";
        public const string PIX_KEY_RANDOM = "random";

        public ProductSettings[] Products { get; set; }
        public string Currency { get; set; }
        public string Channel { get; set; }
        public string Message { get; set; }
        public string Header { get; set; }
        public string Footer { get; set; }
        public PaymentMethodSettings[] PaymentMethods { get; set; }
        public string Type { get; set; } = DIGITAL_GOODS;
        public bool ProcessSavedCardResponse { get; set; }

        public CheckoutSettings[] GetPaymentMethods() => PaymentMethods == null
            ? new[] { this }
            : PaymentMethods.Select(method => method == null ? null : new CheckoutSettings
            {
                PaymentMethod = method.PaymentMethod,
                Provider = method.Provider,
                Customer = method.Customer,
                Merchant = method.Merchant,
                Source = method.Source,
                BotShortName = method.BotShortName,
                PaymentLink = method.PaymentLink,
                BusinessName = method.BusinessName,
                PixKeyType = method.PixKeyType,
                PixKey = method.PixKey,
                PixCode = method.PixCode,
                BankSlipCode = method.BankSlipCode,
                Products = Products,
                Currency = Currency,
                Channel = Channel,
                Message = Message,
                Header = Header,
                Footer = Footer,
                Type = Type
            }).ToArray();

        public void Validate()
        {
            if (PaymentMethods != null)
            {
                if (ProcessSavedCardResponse)
                    throw new ValidationException("Saved-card responses cannot configure PaymentMethods.");

                if (PaymentMethods.Length < 1 || PaymentMethods.Length > 2
                    || PaymentMethods.Any(method => method == null)
                    || PaymentMethods.Select(method => method.PaymentMethod).Distinct().Count() != PaymentMethods.Length)
                    throw new ValidationException("PaymentMethods must contain one or two distinct, non-null payment method settings.");

                foreach (var method in GetPaymentMethods())
                    method.Validate();

                return;
            }

            if (Type != DIGITAL_GOODS && Type != PHYSICAL_GOODS)
                throw new ValidationException($"Type must be '{DIGITAL_GOODS}' or '{PHYSICAL_GOODS}'.");

            if (PaymentMethod != PAYMENT_METHOD_LINK
                && PaymentMethod != PAYMENT_METHOD_PIX
                && PaymentMethod != PAYMENT_METHOD_BANK_SLIP)
                throw new ValidationException("PaymentMethod must be 'paymentLink', 'pix', or 'bankSlip'.");

            if (Provider != PROVIDER_CHECKOUT && Provider != PROVIDER_OWN)
                throw new ValidationException("Provider must be 'checkout' or 'ownProvider'.");

            if (ProcessSavedCardResponse)
            {
                if (Provider != PROVIDER_CHECKOUT || PaymentMethod != PAYMENT_METHOD_LINK)
                    throw new ValidationException("Saved-card responses are only supported for Checkout payment links.");

                return;
            }

            if (PaymentMethod == PAYMENT_METHOD_BANK_SLIP && Provider != PROVIDER_OWN)
                throw new ValidationException("bankSlip is only supported with the ownProvider provider.");

            if (Products == null || Products.Length == 0)
                throw new ValidationException("At least one product is required for Payments.");

            if (string.IsNullOrWhiteSpace(Currency))
                throw new ValidationException("Currency is required for Payments.");

            if (string.IsNullOrWhiteSpace(Channel))
                throw new ValidationException("Channel is required for Payments.");

            if (string.IsNullOrWhiteSpace(Message))
                throw new ValidationException("Message is required for Payments.");

            if (Provider == PROVIDER_CHECKOUT)
            {
                if (string.IsNullOrWhiteSpace(Merchant))
                    throw new ValidationException("Merchant is required for the Checkout provider.");

                if (Customer == null)
                    throw new ValidationException("Customer is required for the Checkout provider.");

                if (string.IsNullOrWhiteSpace(Source))
                    throw new ValidationException("Source is required for the Checkout provider.");

                if (string.IsNullOrWhiteSpace(BotShortName))
                    throw new ValidationException("BotShortName is required for the Checkout provider.");

                return;
            }

            ValidateOwnProviderSettings();
        }

        private void ValidateOwnProviderSettings()
        {
            if (PaymentMethod == PAYMENT_METHOD_LINK)
            {
                if (!Uri.TryCreate(PaymentLink, UriKind.Absolute, out var paymentLinkUri)
                    || (paymentLinkUri.Scheme != Uri.UriSchemeHttp && paymentLinkUri.Scheme != Uri.UriSchemeHttps))
                    throw new ValidationException("PaymentLink must be an absolute HTTP or HTTPS URL for ownProvider.");

            }

            if (PaymentMethod == PAYMENT_METHOD_PIX)
            {
                if (string.IsNullOrWhiteSpace(BusinessName)
                    || string.IsNullOrWhiteSpace(PixKey)
                    || string.IsNullOrWhiteSpace(PixCode))
                    throw new ValidationException("BusinessName, PixKey, and PixCode are required for ownProvider Pix.");

                if (PixKeyType != PIX_KEY_CPF
                    && PixKeyType != PIX_KEY_CNPJ
                    && PixKeyType != PIX_KEY_EMAIL
                    && PixKeyType != PIX_KEY_PHONE
                    && PixKeyType != PIX_KEY_RANDOM)
                    throw new ValidationException("PixKeyType must be 'cpf', 'cnpj', 'email', 'phone', or 'random'.");

            }

            if (PaymentMethod == PAYMENT_METHOD_BANK_SLIP && string.IsNullOrWhiteSpace(BankSlipCode))
                throw new ValidationException("BankSlipCode is required for ownProvider bankSlip.");
        }
    }
}
