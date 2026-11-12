using System.Runtime.Serialization;
using Lime.Protocol;

namespace Take.Blip.Client.Extensions.Builder.Checkout.Documents
{
    public class CustomerLastCardRequest : Document
    {
        public static readonly MediaType MediaType = MediaType.Parse("application/vnd.iris.payment.customer-last-card-request+json");

        public CustomerLastCardRequest() : base(MediaType) { }

        [DataMember(Name = "botShortName")]
        public string BotShortName { get; set; }

        [DataMember(Name = "purchaseId")]
        public string PurchaseId { get; set; }
    }

    public class CustomerLastCardResult : Document
    {
        public static readonly MediaType MediaType = MediaType.Parse("application/vnd.iris.payment.customer-last-card-result+json");

        public CustomerLastCardResult() : base(MediaType) { }

        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "externalId")]
        public string ExternalId { get; set; }

        [DataMember(Name = "lastFourDigits")]
        public string LastFourDigits { get; set; }

        [DataMember(Name = "hasCard")]
        public bool HasCard { get; set; }

        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "errorMessage")]
        public string ErrorMessage { get; set; }
    }

    public class SavedCardPaymentRequest : Document
    {
        public static readonly MediaType MediaType = MediaType.Parse("application/vnd.iris.payment.saved-card-payment-request+json");

        public SavedCardPaymentRequest() : base(MediaType) { }

        [DataMember(Name = "botShortName")]
        public string BotShortName { get; set; }

        [DataMember(Name = "purchaseId")]
        public string PurchaseId { get; set; }

        [DataMember(Name = "cardId")]
        public string CardId { get; set; }

        [DataMember(Name = "installments")]
        public int Installments { get; set; } = 1;
    }
}