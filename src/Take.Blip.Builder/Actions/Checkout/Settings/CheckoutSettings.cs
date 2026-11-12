using System.ComponentModel.DataAnnotations;
using Take.Blip.Builder.Models;

namespace Take.Blip.Builder.Actions.Checkout.Settings
{
    /// <summary>
    /// Represents the configuration settings required to perform a checkout operation, including customer, product,
    /// merchant, and transaction details.
    /// </summary>
    /// <remarks>All properties must be set with valid values before initiating a checkout. This class is
    /// typically used to encapsulate all necessary information for processing a checkout request in a payment or
    /// e-commerce workflow.</remarks>
    public class CheckoutSettings : IValidable
    {
        /// <summary>
        /// Gets or sets the customer-specific settings for the application.
        /// </summary>
        public CustomerSettings Customer { get; set; }

        /// <summary>
        /// Gets or sets the collection of product settings configured for the application.
        /// </summary>
        public ProductSettings[] Products { get; set; }

        /// <summary>
        /// Gets or sets the merchant identifier associated with the transaction.
        /// </summary>
        public string Merchant { get; set; }

        /// <summary>
        /// Gets or sets the currency associated with the transaction or entity.
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Gets or sets the name of the channel associated with this instance.
        /// </summary>
        public string Channel { get; set; }

        /// <summary>
        /// Gets or sets the source identifier associated with this instance.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the message content associated with this instance.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the bot short name associated with this instance.
        /// </summary>
        public string BotShortName { get; set; }

        /// <summary>
        /// Processes the customer's saved-card choice instead of creating a new checkout.
        /// </summary>
        public bool ProcessSavedCardResponse { get; set; }

        /// <summary>
        /// Context variable used to correlate a saved-card choice with its pending purchase.
        /// </summary>
        public string PendingCheckoutVariable { get; set; } = "checkout.pending";
  

        /// <summary>
        /// Validates that all required properties for the checkout action are set.
        /// </summary>
        /// <remarks>Call this method before performing the checkout action to ensure that all necessary
        /// information has been provided. This method does not perform deep validation of property values beyond
        /// checking for null references.</remarks>
        /// <exception cref="ValidationException">Thrown if any required property (Merchant, Customer, Products, Currency, Channel, Source, or Message) is
        /// null.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(PendingCheckoutVariable))
            {
                throw new ValidationException($"The '{nameof(PendingCheckoutVariable)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (ProcessSavedCardResponse)
            {
                return;
            }

            if (Merchant == null)
            {
                throw new ValidationException($"The '{nameof(Merchant)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Customer == null)
            {
                throw new ValidationException($"The '{nameof(Customer)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Products == null)
            {
                throw new ValidationException($"The '{nameof(Products)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Currency == null)
            {
                throw new ValidationException($"The '{nameof(Currency)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Channel == null)
            {
                throw new ValidationException($"The '{nameof(Channel)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Source == null)
            {
                throw new ValidationException($"The '{nameof(Source)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (Message == null)
            {
                throw new ValidationException($"The '{nameof(Message)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (BotShortName == null)
            {
                throw new ValidationException($"The '{nameof(BotShortName)}' setting value is required for '{nameof(Checkout)}' action");
            }
        }
    }
}