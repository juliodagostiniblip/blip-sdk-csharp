using Xunit;
using Take.Blip.Builder.Actions.Checkout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Take.Blip.Client.Extensions.Builder.Checkout;
using Take.Blip.Client.Extensions.Builder.Checkout.Documents;
using Take.Blip.Client;
using Serilog;
using NSubstitute;
using Take.Blip.Builder.Actions.Checkout.Settings;
using Lime.Protocol;
using System.Threading;
using Lime.Messaging.Contents;
using Take.Blip.Builder.Models;
using Lime.Protocol.Serialization;
using Take.Blip.Client.Extensions.ArtificialIntelligence;

namespace Take.Blip.Builder.Actions.Checkout.Tests
{
    public class CheckoutUnitTests
    {
        private readonly ICheckoutExtension _checkoutExtension;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        private readonly BuilderConfiguration _builderConfiguration;
        private readonly IDocumentSerializer _documentSerializer;
        private readonly IEnvelopeSerializer _envelopeSerializer;
        private readonly IArtificialIntelligenceExtension _artificialIntelligenceExtension;

        private readonly CheckoutAction _checkoutAction;

        public CheckoutUnitTests()
        {
            _checkoutExtension = Substitute.For<ICheckoutExtension>();
            _sender = Substitute.For<ISender>();
            _logger = Substitute.For<ILogger>();

            _builderConfiguration = Substitute.For<BuilderConfiguration>();
            _documentSerializer = Substitute.For<IDocumentSerializer>();
            _envelopeSerializer = Substitute.For<IEnvelopeSerializer>();
            _artificialIntelligenceExtension = Substitute.For<IArtificialIntelligenceExtension>();

            _checkoutAction = new CheckoutAction(_checkoutExtension, _sender, _logger);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldCreateCheckoutLinkAndSendMessage()
        {
            // Arrange  
            var context = Substitute.For<IContext>();
            var settings = new CheckoutSettings
            {
                Customer = new CustomerSettings
                {
                    Identity = "customer@domain.com",
                    Phone = "123456789",
                    Name = "John",
                    Surname = "Doe",
                    Email = "john.doe@domain.com",
                    Document = "123456789",
                    DocumentType = "CPF"
                },
                Products = new[]
                {
                       new ProductSettings { Description = "Product 1", Price = 10.0m, Quantity = 1 },
                       new ProductSettings { Description = "Product 2", Price = 20.0m, Quantity = 2 }
                   },
                Channel = "WhatsApp",
                Currency = "BRL",
                Source = "TestSource",
                Message = "Mensagem de Teste",
                Merchant = "1234"
            };

            var cancellationToken = CancellationToken.None;
            var checkoutLink = "https://teste-media.msging.net";

            _checkoutExtension.CreateCheckOutLinkAsync(
                Arg.Is(settings.Merchant),
                Arg.Any<Take.Blip.Client.Extensions.Builder.Checkout.Documents.CheckoutDocument>(),
                cancellationToken)
                .Returns(checkoutLink);

            var node = new Node("teste", "wa.gw.msging.net", "#instance");

            var message = new Message()
            {
                From = new Node("teste", "wa.gw.msging.net", "#instance"),
                To = new Node("mybot", "msging.net", null),
                Content = new PlainText { Text = "Hello BLiP" }
            };

            var input = new LazyInput(message,
                message.From,
                _builderConfiguration,
                _documentSerializer,
                _envelopeSerializer,
                _artificialIntelligenceExtension,
                cancellationToken);

            context.Input.Returns(input);

            // Act  
            await _checkoutAction.ExecuteAsync(context, settings, cancellationToken);

            // Assert  
            await _checkoutExtension
                .Received(1)
                .CreateCheckOutLinkAsync(
                    Arg.Any<string>(),
                    Arg.Any<Take.Blip.Client.Extensions.Builder.Checkout.Documents.CheckoutDocument>(),
                    cancellationToken);

            await _sender
                .Received(1)
                .SendMessageAsync(Arg.Is<Message>(m =>
                    m.Content is JsonDocument), cancellationToken);
        }

        [Fact]
        public async Task ExecuteAsync_WithSavedCard_ShouldSendChoiceWithoutCharging()
        {
            var context = CreateContext();
            ConfigureSavedCard();

            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);

            var select = Assert.IsType<Select>(GetLastMessage().Content);
            Assert.Contains("1234", select.Text);
            Assert.Equal(2, select.Options.Length);
            await _checkoutExtension.Received(1).GetCustomerLastCardAsync(
                Arg.Is<CustomerLastCardRequest>(request => request.PurchaseId == "purchase-1" && request.BotShortName == "bot"),
                CancellationToken.None);
            await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExecuteAsync_ShouldProcessChoiceUsingOriginalPurchase(bool useSavedCard)
        {
            var context = CreateContext();
            ConfigureSavedCard();
            _checkoutExtension.PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument { ["success"] = true });
            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);
            var select = Assert.IsType<Select>(GetLastMessage().Content);
            context.Input.Message.Content = select.Options[useSavedCard ? 0 : 1].Value;

            var responseSettings = new CheckoutSettings { ProcessSavedCardResponse = true };
            responseSettings.Validate();
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);

            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<CheckoutDocument>(), Arg.Any<CancellationToken>());
            if (useSavedCard)
            {
                await _checkoutExtension.Received(1).PayWithSavedCardAsync(
                    Arg.Is<SavedCardPaymentRequest>(request => request.PurchaseId == "purchase-1"
                        && request.CardId == "card-1" && request.BotShortName == "bot" && request.Installments == 1),
                    CancellationToken.None);
                await _sender.Received(1).SendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
            }
            else
            {
                await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
                var order = Assert.IsType<JsonDocument>(GetLastMessage().Content);
                var interactive = Assert.IsType<JsonDocument>(order["interactive"]);
                Assert.Equal("order_details", interactive["type"]);
                await _sender.Received(2).SendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
            }
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task ExecuteAsync_WithoutUsableCard_ShouldSendOrder(bool hasCard, bool success)
        {
            var context = CreateContext();
            ConfigureSavedCard();
            _checkoutExtension.GetCustomerLastCardAsync(Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CustomerLastCardResult { HasCard = hasCard, Success = success });

            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);

            Assert.IsType<JsonDocument>(GetLastMessage().Content);
            await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("Pagar com cartao salvo")]
        [InlineData("checkout:another-purchase:saved-card")]
        public async Task ExecuteAsync_WithUnrelatedResponse_ShouldNotCharge(string response)
        {
            var context = CreateContext();
            ConfigureSavedCard();
            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);
            context.Input.Message.Content = new PlainText { Text = response };

            await _checkoutAction.ExecuteAsync(context, new CheckoutSettings { ProcessSavedCardResponse = true }, CancellationToken.None);

            await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
            await _sender.Received(1).SendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_WhenCardLookupFails_ShouldSendOrder()
        {
            var context = CreateContext();
            ConfigureSavedCard();
            _checkoutExtension.GetCustomerLastCardAsync(Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<CustomerLastCardResult>(new InvalidOperationException("Unavailable")));

            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);

            Assert.IsType<JsonDocument>(GetLastMessage().Content);
        }

        [Fact]
        public async Task ExecuteAsync_WhenCardLookupIsCancelled_ShouldPropagateCancellation()
        {
            var context = CreateContext();
            ConfigureSavedCard();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            _checkoutExtension.GetCustomerLastCardAsync(Arg.Any<CustomerLastCardRequest>(), cancellation.Token)
                .Returns(Task.FromCanceled<CustomerLastCardResult>(cancellation.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _checkoutAction.ExecuteAsync(context, CreateSettings(), cancellation.Token));

            await _sender.DidNotReceive().SendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckoutExtension_ShouldSendSavedCardCommands(bool lookup)
        {
            var extension = new CheckoutExtension(_sender);
            Document request = lookup
                ? (Document)new CustomerLastCardRequest { BotShortName = "bot", PurchaseId = "purchase-1" }
                : new SavedCardPaymentRequest { BotShortName = "bot", PurchaseId = "purchase-1", CardId = "card-1" };
            Document response = lookup ? (Document)new CustomerLastCardResult { Success = true } : new JsonDocument { ["success"] = true };
            _sender.ProcessCommandAsync(Arg.Any<Command>(), CancellationToken.None)
                .Returns(new Command { Status = CommandStatus.Success, Resource = response });

            var result = lookup
                ? await extension.GetCustomerLastCardAsync((CustomerLastCardRequest)request, CancellationToken.None)
                : await extension.PayWithSavedCardAsync((SavedCardPaymentRequest)request, CancellationToken.None);

            Assert.Same(response, result);
            var expectedUri = lookup ? "/iris/payment/customer/last-card" : "/iris/payment/saved-card";
            await _sender.Received(1).ProcessCommandAsync(Arg.Is<Command>(command =>
                command.Method == CommandMethod.Set && command.Uri.ToString() == expectedUri
                && command.To.ToString() == "postmaster@payment.msging.net/#MAGNA-802Y194"
                && !string.IsNullOrWhiteSpace(command.Id) && command.Resource == request), CancellationToken.None);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExecuteAsync_WhenPaymentFails_ShouldNotRetryConsumedChoice(bool emptyResponse)
        {
            var context = CreateContext();
            ConfigureSavedCard();
            _checkoutExtension.PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>())
                .Returns(emptyResponse ? (Document)null : new JsonDocument { ["success"] = false });
            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);
            context.Input.Message.Content = Assert.IsType<Select>(GetLastMessage().Content).Options[0].Value;
            var responseSettings = new CheckoutSettings { ProcessSavedCardResponse = true };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None));
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);

            await _checkoutExtension.Received(1).PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        private void ConfigureSavedCard()
        {
            _checkoutExtension.CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<CheckoutDocument>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument { ["paymentLink"] = "https://payment.example/checkout", ["checkoutId"] = "purchase-1" });
            _checkoutExtension.GetCustomerLastCardAsync(Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CustomerLastCardResult { Success = true, HasCard = true, Id = "card-1", LastFourDigits = "1234" });
        }

        private Message GetLastMessage() => _sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISender.SendMessageAsync))
            .Select(call => (Message)call.GetArguments()[0]).Last();

        private IContext CreateContext()
        {
            var context = Substitute.For<IContext>();
            var variables = new Dictionary<string, string>();
            context.SetVariableAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan>())
                .Returns(call =>
                {
                    variables[(string)call[0]] = (string)call[1];
                    return Task.CompletedTask;
                });
            context.GetContextVariableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(variables.TryGetValue((string)call[0], out var value) ? value : null));
            context.DeleteVariableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    variables.Remove((string)call[0]);
                    return Task.CompletedTask;
                });
            var message = new Message
            {
                From = Node.Parse("customer@wa.gw.msging.net"),
                Content = new PlainText { Text = "checkout" }
            };
            context.Input.Returns(new LazyInput(message, message.From, _builderConfiguration, _documentSerializer,
                _envelopeSerializer, _artificialIntelligenceExtension, CancellationToken.None));
            return context;
        }

        private static CheckoutSettings CreateSettings() => new CheckoutSettings
        {
            Customer = new CustomerSettings { Identity = "customer@wa.gw.msging.net" },
            Products = new[] { new ProductSettings { Description = "Product", Price = 10m, Quantity = 1 } },
            Channel = "WhatsApp",
            Currency = "BRL",
            Source = "Test",
            Message = "Pagamento",
            Merchant = "merchant",
            BotShortName = "bot"
        };
    }
}