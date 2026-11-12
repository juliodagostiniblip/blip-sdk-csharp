using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Take.Blip.Client.Extensions.Builder.Checkout.Documents
{
    /// <summary>
    /// Represents customer information required during the checkout process.
    /// </summary>
    /// <remarks>This class is typically used to capture and transfer customer details such as identity,
    /// contact information, and identification documents when processing a checkout. All properties are optional unless
    /// otherwise specified by the consuming API or service.</remarks>
    public class CustomerCheckout
    {
        /// <summary>
        /// Gets or sets the unique identifier associated with this instance.
        /// </summary>
        [DataMember(Name = "identity")]
        public string Identity { get; set; }

        /// <summary>
        /// Gets or sets the phone number associated with the entity.
        /// </summary>
        [DataMember(Name = "phone")]
        public string Phone { get; set; }

        /// <summary>
        /// Gets or sets the name associated with the object.
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the surname of the person.
        /// </summary>
        [DataMember(Name = "surname")]
        public string Surname { get; set; }

        /// <summary>
        /// Gets or sets the email address associated with the entity.
        /// </summary>
        [DataMember(Name = "email")]
        public string Email { get; set; }

        /// <summary>
        /// Gets or sets the type of the document.
        /// </summary>
        [DataMember(Name = "documentType")]
        public string DocumentType { get; set; }

        /// <summary>
        /// Gets or sets the document content as a string.
        /// </summary>
        [DataMember(Name = "document")]
        public string Document { get; set; }
    }

}
