using Take.Blip.Builder.Actions.Checkout.Settings;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Represents the data required to process a pending checkout, including checkout settings and related
    /// identifiers.
    /// </summary>
    internal sealed class PendingCheckout
    {
        public CheckoutSettings Settings { get; set; }
        public string PurchaseId { get; set; }
        public string CardId { get; set; }
        public string ChoiceId { get; set; }
        public string CheckoutLink { get; set; }
        public string ReferenceId { get; set; }
    }
}
