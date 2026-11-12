using System.ComponentModel.DataAnnotations;
using Take.Blip.Builder.Models;

namespace Take.Blip.Builder.Actions.Checkout.Settings
{
    /// <summary>
    /// Represents the customer information and settings required for processing a checkout operation.
    /// </summary>
    /// <remarks>This class encapsulates customer details such as identity, contact information, and document
    /// data. It is typically used to provide the necessary customer context when performing actions that require
    /// customer validation, such as checkout processes. All required fields must be populated before
    /// validation.</remarks>
    public class CustomerSettings : IValidable
    {
        /// <summary>
        /// Gets or sets the unique identifier associated with this instance.
        /// </summary>
        public string Identity { get; set; }

        /// <summary>
        /// Gets or sets the phone number associated with the entity.
        /// </summary>
        public string Phone { get; set; }

        /// <summary>
        /// Gets or sets the name associated with the object.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the surname of the person.
        /// </summary>
        public string Surname { get; set; }

        /// <summary>
        /// Gets or sets the email address associated with the user.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Gets or sets the type of the document.
        /// </summary>
        public string DocumentType { get; set; }

        /// <summary>
        /// Gets or sets the document content as a string.
        /// </summary>
        public string Document { get; set; }

        /// <summary>
        /// Validates that all required customer information is present before performing a checkout action.
        /// </summary>
        /// <remarks>Call this method before initiating a checkout to ensure that the customer data is
        /// complete. This method checks for required fields and throws an exception if any are missing, preventing
        /// further processing with incomplete information.</remarks>
        /// <exception cref="ValidationException">Thrown if the required customer identity or phone number is missing or consists only of white-space
        /// characters.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Identity))
            {
                throw new ValidationException($"The 'Customer.{nameof(Identity)}' setting value is required for '{nameof(Checkout)}' action");
            }

            if (string.IsNullOrWhiteSpace(Phone))
            {
                throw new ValidationException($"The 'Customer.{nameof(Phone)}' setting value is required for '{nameof(Checkout)}' action");
            }
        }
    }
}

