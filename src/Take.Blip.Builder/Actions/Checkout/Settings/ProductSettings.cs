using System.ComponentModel.DataAnnotations;
using Take.Blip.Builder.Models;

namespace Take.Blip.Builder.Actions.Checkout.Settings
{
    /// <summary>
    /// Represents the configurable settings for a product, including description, quantity, and price information.
    /// </summary>
    /// <remarks>Use this class to encapsulate product-related configuration data required for operations such
    /// as checkout. The settings should be validated before use to ensure they meet the required constraints.</remarks>
    public class ProductSettings : IValidable
    {
        /// <summary>
        /// Gets or sets the description associated with the object.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the quantity associated with the current instance.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Gets or sets the price of the item.
        /// </summary>
        public decimal Price { get; set; }        
        
        /// <summary>
        /// Validates the product settings required for the checkout action.
        /// </summary>
        /// <remarks>Call this method before performing a checkout operation to ensure that all required
        /// product settings are valid. This method checks for missing or invalid values and throws an exception if any
        /// validation fails.</remarks>
        /// <exception cref="ValidationException">Thrown if the product description is null, empty, or whitespace; if the quantity is less than or equal to
        /// zero; or if the price is negative.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Description))
            {
                throw new ValidationException($"The 'Product.{nameof(Description)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Quantity <= 0)
            {
                throw new ValidationException($"The 'Product.{nameof(Quantity)}' setting value must be greater than zero for '{nameof(Checkout)}' action");
            }

            if (Price < 0)
            {
                throw new ValidationException($"The 'Product.{nameof(Price)}' setting value cannot be negative for '{nameof(Checkout)}' action");
            }
        }
    }
}
