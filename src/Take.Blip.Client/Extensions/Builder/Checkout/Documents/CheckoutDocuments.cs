using System.Collections.Generic;
using System.Runtime.Serialization;
using Lime.Protocol;

namespace Take.Blip.Client.Extensions.Builder.Checkout.Documents
{
    /// <summary>
    /// Represents a document containing the information required to initiate a checkout process, including customer
    /// details, products, and payment context.
    /// </summary>
    /// <remarks>Use this class to serialize or deserialize checkout requests for payment processing systems
    /// that support the specified media type. The document includes customer information, a list of products to be
    /// purchased, and contextual data such as currency and payment channel.</remarks>
    public class CheckoutDocument : Document
    {
        /// <summary>
        /// Represents the MIME type for an Iris payment checkout link request in JSON format.
        /// </summary>
        /// <remarks>Use this constant when specifying the content type for HTTP requests or responses
        /// related to Iris payment checkout link operations.</remarks>
        public const string MIME_TYPE = "application/vnd.iris.payment.checkout-link-request+json";

        /// <summary>
        /// Represents the media type associated with this instance.
        /// </summary>
        /// <remarks>Use this field to access the parsed media type for operations that require content
        /// type information. The value is initialized by parsing the MIME type string defined by the associated
        /// constant.</remarks>
        public static readonly MediaType MediaType = MediaType.Parse(MIME_TYPE);

        /// <summary>
        /// Initializes a new instance of the CheckoutDocument class with the default media type.
        /// </summary>
        public CheckoutDocument() : base(MediaType)
        {

        }

        /// <summary>
        /// Gets or sets the customer information associated with the checkout process.
        /// </summary>
        [DataMember(Name = "customer")]
        public CustomerCheckout Customer { get; set; }

        /// <summary>
        /// Gets or sets the collection of products included in the checkout.
        /// </summary>
        [DataMember(Name = "products")]
        public List<ProductCheckout> Products { get; set; }

        /// <summary>
        /// Gets or sets the ISO currency code associated with the transaction or entity.
        /// </summary>
        /// <remarks>The currency code should follow the ISO 4217 standard (for example, "BRL" for Brazilian Reais
        /// or "EUR" for euros).</remarks>
        [DataMember(Name = "currency")]
        public string Currency { get; set; }

        /// <summary>
        /// Gets or sets the name of the channel associated with this instance.
        /// </summary>
        [DataMember(Name = "channel")]
        public string Channel { get; set; }

        /// <summary>
        /// Gets or sets the source identifier associated with the data.
        /// </summary>
        [DataMember(Name = "source")]
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the short name of the bot.
        /// </summary>
        [DataMember(Name = "botShortName")]
        public string BotShortName { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for the merchant associated with this entity.
        /// </summary>
        [DataMember(Name = "merchantId")]
        public string MerchantId { get; set; }
    }
}