using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Take.Blip.Client.Extensions.Builder.Checkout.Documents
{
    /// <summary>
    /// Represents a product item included in a checkout transaction, including its description, price, and quantity.
    /// </summary>
    public class ProductCheckout
    {
        /// <summary>
        /// Gets or sets the description associated with the object.
        /// </summary>
        [DataMember(Name = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the price associated with the item.
        /// </summary>
        [DataMember(Name = "price")]
        public decimal Price { get; set; }

        /// <summary>
        /// Gets or sets the quantity associated with the current instance.
        /// </summary>
        [DataMember(Name = "quantity")]
        public int Quantity { get; set; }
    }
}
