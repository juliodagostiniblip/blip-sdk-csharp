using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol;
using Take.Blip.Client.Extensions.Builder.Checkout.Documents;

namespace Take.Blip.Client.Extensions.Builder.Checkout
{
    /// <summary>
    /// Defines a contract for creating a checkout link for a specified merchant and checkout document.
    /// </summary>
    /// <remarks>Implementations of this interface are responsible for generating checkout links that can be
    /// used to initiate payment or order processes. The interface is intended to be used in scenarios where different
    /// checkout providers or mechanisms may be integrated.</remarks>
    public interface ICheckoutExtension
    {
        /// <summary>
        /// Creates a checkout link for the specified merchant and checkout document asynchronously.
        /// </summary>
        /// <param name="merchant">The unique identifier of the merchant for whom the checkout link is being created. Cannot be null or empty.</param>
        /// <param name="document">The checkout document containing the details of the transaction. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a document representing the
        /// created checkout link.</returns>
        Task<Document> CreateCheckOutLinkAsync(string merchant, CheckoutDocument document, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves the customer's last saved card for an existing purchase.
        /// </summary>
        Task<CustomerLastCardResult> GetCustomerLastCardAsync(CustomerLastCardRequest document, CancellationToken cancellationToken);

        /// <summary>
        /// Processes an existing purchase using a saved card after customer confirmation.
        /// </summary>
        Task<Document> PayWithSavedCardAsync(SavedCardPaymentRequest document, CancellationToken cancellationToken);
    }
}
