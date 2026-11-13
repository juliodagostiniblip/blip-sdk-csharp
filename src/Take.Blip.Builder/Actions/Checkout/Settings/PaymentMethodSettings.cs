namespace Take.Blip.Builder.Actions.Checkout.Settings
{
    public class PaymentMethodSettings
    {
        public string PaymentMethod { get; set; } = CheckoutSettings.PAYMENT_METHOD_LINK;
        public string Provider { get; set; } = CheckoutSettings.PROVIDER_CHECKOUT;
        public CustomerSettings Customer { get; set; }
        public string Merchant { get; set; }
        public string Source { get; set; }
        public string BotShortName { get; set; }
        public string PaymentLink { get; set; }
        public string BusinessName { get; set; }
        public string PixKeyType { get; set; }
        public string PixKey { get; set; }
        public string PixCode { get; set; }
        public string BankSlipCode { get; set; }
    }
}