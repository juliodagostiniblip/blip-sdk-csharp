using System;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol;
using Take.Blip.Client.Extensions.Builder.Checkout.Documents;

namespace Take.Blip.Client.Extensions.Builder.Checkout
{
    /// <summary>
    /// Provides functionality for creating checkout links for merchants using the checkout service.
    /// </summary>
    /// <remarks>This extension enables integration with the checkout system by allowing clients to generate
    /// checkout links for specific merchants. It is intended to be used in scenarios where a merchant needs to provide
    /// a customer with a unique checkout URL. Thread safety depends on the underlying sender implementation.</remarks>
    public class CheckoutExtension : ExtensionBase, ICheckoutExtension
    {
        private static readonly Node _paymentAddress = Node.Parse($"postmaster@payment.{Constants.DEFAULT_DOMAIN}");
        private const string CHECKOUT_LINK_CREATION = "/iris/payment/link";
        private const string CUSTOMER_LAST_CARD = "/iris/payment/customer/last-card";
        private const string PAY_WITH_SAVED_CARD = "/iris/payment/saved-card";

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckoutExtension"/> class.
        /// </summary>
        /// <param name="sender">The sender used to send commands.</param>
        public CheckoutExtension(ISender sender) : base(sender)
        {
        }

        /// <summary>
        /// Creates a checkout link for the specified merchant using the provided checkout document.
        /// </summary>
        /// <param name="merchant">The unique identifier of the merchant for whom the checkout link is being created. Cannot be null or empty.</param>
        /// <param name="document">The checkout document containing the details required to generate the checkout link. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a Document representing the
        /// created checkout link.</returns>
        public async Task<Document> CreateCheckOutLinkAsync(string merchant, CheckoutDocument document, CancellationToken cancellationToken)
        {
            var requestCommand = new Command()
            {
                Id = EnvelopeId.NewId(),
                To = _paymentAddress,
                Method = CommandMethod.Set,
                Uri = new LimeUri(CHECKOUT_LINK_CREATION),
                Resource = document
            };

            var response = await ProcessCommandAsync<Document>(requestCommand, cancellationToken);

            return response;
        }

        /// <inheritdoc />
        public Task<CustomerLastCardResult> GetCustomerLastCardAsync(CustomerLastCardRequest document, CancellationToken cancellationToken)
        {
            return ProcessCommandAsync<CustomerLastCardResult>(
                CreateSetCommandRequest(document, CUSTOMER_LAST_CARD, _paymentAddress), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Document> PayWithSavedCardAsync(SavedCardPaymentRequest document, CancellationToken cancellationToken)
        {
            var requestCommand = new Command()
            {
                Id = EnvelopeId.NewId(),
                To = _paymentAddress,
                Method = CommandMethod.Set,
                Uri = new LimeUri(PAY_WITH_SAVED_CARD),
                Resource = document
            };

            var response = await ProcessCommandAsync<Document>(requestCommand, cancellationToken);

            return response;
        }
    }
}
