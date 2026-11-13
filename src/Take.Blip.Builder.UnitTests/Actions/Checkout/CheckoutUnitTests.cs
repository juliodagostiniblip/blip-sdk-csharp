using Xunit;
using Take.Blip.Builder.Actions.Checkout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Take.Blip.Client.Extensions.Builder.Checkout;
using Lime.Messaging.Resources.Checkout;
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
                Header = "Resumo do pedido",
                Footer = "Obrigado pela preferência",
                Type = CheckoutSettings.PHYSICAL_GOODS,
                Merchant = "1234"
            };

            var cancellationToken = CancellationToken.None;
            var checkoutLink = "https://teste-media.msging.net";

            _checkoutExtension.CreateCheckOutLinkAsync(
                Arg.Is(settings.Merchant),
                Arg.Any<PaymentCheckoutRequest>(),
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
                    Arg.Any<PaymentCheckoutRequest>(),
                    cancellationToken);

            await _sender
                .Received(1)
                .SendMessageAsync(Arg.Is<Message>(m =>
                    m.Content is JsonDocument), cancellationToken);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var parameters = Assert.IsType<JsonDocument>(Assert.IsType<JsonDocument>(interactive["action"])["parameters"]);
            Assert.Equal(CheckoutSettings.PHYSICAL_GOODS, parameters["type"]);
            var header = Assert.IsType<JsonDocument>(interactive["header"]);
            Assert.Equal("text", header["type"]);
            Assert.Equal("Resumo do pedido", header["text"]);
            var footer = Assert.IsType<JsonDocument>(interactive["footer"]);
            Assert.Equal("Obrigado pela preferência", footer["text"]);
        }

        [Fact]
        public async Task ExecuteAsync_WithSavedCard_ShouldSendChoiceWithoutCharging()
        {
            var context = CreateContext();
            ConfigureSavedCard();

            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);

            var content = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(content["interactive"]);
            Assert.Equal("button", interactive["type"]);
            var body = Assert.IsType<JsonDocument>(interactive["body"]);
            Assert.Contains("Resumo do seu pedido", body["text"].ToString());
            var action = Assert.IsType<JsonDocument>(interactive["action"]);
            var buttons = Assert.IsType<object[]>(action["buttons"]);
            Assert.Equal(2, buttons.Length);

            var firstButton = Assert.IsType<JsonDocument>(buttons[0]);
            var firstReply = Assert.IsType<JsonDocument>(firstButton["reply"]);
            Assert.Equal("PAY_ONE_CLICK_card-1", firstReply["id"]);
            Assert.Contains("1234", firstReply["title"].ToString());

            var secondButton = Assert.IsType<JsonDocument>(buttons[1]);
            var secondReply = Assert.IsType<JsonDocument>(secondButton["reply"]);
            Assert.Equal("ACCESS_PAYMENT_LINK", secondReply["id"]);
            Assert.Equal("Outra forma", secondReply["title"]);

            await context.Received(1).SetVariableAsync(
                "checkoutPending",
                Arg.Any<string>(),
                CancellationToken.None,
                TimeSpan.FromMinutes(30));
            await _checkoutExtension.Received(1).GetCustomerLastCardAsync(
                Arg.Is<CustomerLastCardRequest>(request => request.PurchaseId == "purchase-1" && request.BotShortName == "bot"),
                CancellationToken.None);
            await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, "root")]
        [InlineData(false, "root")]
        [InlineData(true, "metadata")]
        [InlineData(false, "metadata")]
        [InlineData(true, "interactive")]
        [InlineData(false, "interactive")]
        [InlineData(true, "button_reply")]
        [InlineData(false, "button_reply")]
        [InlineData(false, "title")]
        [InlineData(true, "title")]
        public async Task ExecuteAsync_ShouldProcessChoiceUsingOriginalPurchase(bool useSavedCard, string responseFormat)
        {
            var context = CreateContext();
            ConfigureSavedCard();
            _checkoutExtension.PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument { ["success"] = true });
            await _checkoutAction.ExecuteAsync(context, CreateSettings(), CancellationToken.None);
            var responseId = useSavedCard ? "PAY_ONE_CLICK_card-1" : "ACCESS_PAYMENT_LINK";
            var buttonReply = new JsonDocument { ["id"] = responseId, ["title"] = useSavedCard ? "Cartao final 1234" : "Outra forma" };
            switch (responseFormat)
            {
                case "title":
                    context.Input.Message.Content = new PlainText { Text = useSavedCard ? "Cartão final 1234" : "Outra forma" };
                    break;
                case "metadata":
                    context.Input.Message.Content = new PlainText { Text = useSavedCard ? "Cartao final 1234" : "Outra forma" };
                    context.Input.Message.Metadata = new Dictionary<string, string>
                    {
                        ["#wa.interactive.button_reply.id"] = responseId
                    };
                    break;
                case "interactive":
                    context.Input.Message.Content = new JsonDocument
                    {
                        ["interactive"] = new JsonDocument { ["type"] = "button_reply", ["button_reply"] = buttonReply }
                    };
                    break;
                case "button_reply":
                    context.Input.Message.Content = new JsonDocument { ["type"] = "button_reply", ["button_reply"] = buttonReply };
                    break;
                default:
                    context.Input.Message.Content = buttonReply;
                    break;
            }

            var responseSettings = new CheckoutSettings { ProcessSavedCardResponse = true };
            responseSettings.Validate();
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);

            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>());
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

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var parameters = Assert.IsType<JsonDocument>(Assert.IsType<JsonDocument>(interactive["action"])["parameters"]);
            Assert.Equal(CheckoutSettings.DIGITAL_GOODS, parameters["type"]);
            Assert.False(interactive.ContainsKey("header"));
            Assert.False(interactive.ContainsKey("footer"));
            await _checkoutExtension.DidNotReceive().PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public void CheckoutSettings_RejectsUnsupportedOrderType()
        {
            var settings = CreateSettings();
            settings.Type = "service";

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        [Fact]
        public void CheckoutSettings_RejectsBankSlipWithCheckoutProvider()
        {
            var settings = CreateSettings();
            settings.PaymentMethod = CheckoutSettings.PAYMENT_METHOD_BANK_SLIP;

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        [Fact]
        public void ActionType_IsPayments()
        {
            Assert.Equal("Payments", _checkoutAction.Type);
        }

        [Fact]
        public async Task ExecuteAsync_OwnProviderPaymentLink_UsesSuppliedUrlWithoutCallingCheckout()
        {
            var context = CreateContext();
            var settings = CreateOwnProviderSettings(CheckoutSettings.PAYMENT_METHOD_LINK);
            settings.PaymentLink = "https://merchant.example/pay/123";

            await _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var parameters = GetOrderParameters(interactive);
            var paymentSetting = GetFirstPaymentSetting(parameters);
            var paymentLink = Assert.IsType<JsonDocument>(paymentSetting["payment_link"]);
            Assert.Equal("payment_link", paymentSetting["type"]);
            Assert.Equal("https://merchant.example/pay/123", paymentLink["uri"]);
            await AssertCheckoutWasNotCalledAsync();
        }

        [Fact]
        public async Task ExecuteAsync_OwnProviderPix_UsesSuppliedPixDataWithoutCallingCheckout()
        {
            var context = CreateContext();
            var settings = CreateOwnProviderSettings(CheckoutSettings.PAYMENT_METHOD_PIX);
            settings.BusinessName = "Example Store";
            settings.PixKeyType = CheckoutSettings.PIX_KEY_EMAIL;
            settings.PixKey = "payments@example.com";
            settings.PixCode = "000201-pix-copy-code";

            await _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var paymentSetting = GetFirstPaymentSetting(GetOrderParameters(interactive));
            var pix = Assert.IsType<JsonDocument>(paymentSetting["pix_dynamic_code"]);
            Assert.Equal("pix_dynamic_code", paymentSetting["type"]);
            Assert.Equal("000201-pix-copy-code", pix["code"]);
            Assert.Equal("Example Store", pix["merchant_name"]);
            Assert.Equal("payments@example.com", pix["key"]);
            Assert.Equal("EMAIL", pix["key_type"]);
            await AssertCheckoutWasNotCalledAsync();
        }

        [Theory]
        [InlineData("cpf", "CPF")]
        [InlineData("cnpj", "CNPJ")]
        [InlineData("email", "EMAIL")]
        [InlineData("phone", "PHONE")]
        [InlineData("random", "RANDOM_KEY")]
        public async Task ExecuteAsync_OwnProviderPix_MapsPixKeyTypes(string pixKeyType, string metaKeyType)
        {
            var context = CreateContext();
            var settings = CreateOwnProviderSettings(CheckoutSettings.PAYMENT_METHOD_PIX);
            settings.BusinessName = "Example Store";
            settings.PixKeyType = pixKeyType;
            settings.PixKey = "test-key";
            settings.PixCode = "pix-code";

            await _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var paymentSetting = GetFirstPaymentSetting(GetOrderParameters(interactive));
            var pix = Assert.IsType<JsonDocument>(paymentSetting["pix_dynamic_code"]);
            Assert.Equal(metaKeyType, pix["key_type"]);
        }

        [Fact]
        public async Task ExecuteAsync_OwnProviderBankSlip_UsesSuppliedCodeWithoutCallingCheckout()
        {
            var context = CreateContext();
            var settings = CreateOwnProviderSettings(CheckoutSettings.PAYMENT_METHOD_BANK_SLIP);
            settings.BankSlipCode = "00190.00009 01234.567890 12345.678901 2 12340000025000";

            await _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var paymentSetting = GetFirstPaymentSetting(GetOrderParameters(interactive));
            var boleto = Assert.IsType<JsonDocument>(paymentSetting["boleto"]);
            Assert.Equal("boleto", paymentSetting["type"]);
            Assert.Equal("00190000090123456789012345678901212340000025000", boleto["digitable_line"]);
            await AssertCheckoutWasNotCalledAsync();
        }

        [Fact]
        public async Task ExecuteAsync_CheckoutPix_UsesPixReturnedByPaymentApplication()
        {
            var context = CreateContext();
            var settings = CreateSettings();
            settings.PaymentMethod = CheckoutSettings.PAYMENT_METHOD_PIX;
            _checkoutExtension.CreateCheckOutLinkAsync(
                    Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>()
                )
                .Returns(new JsonDocument
                {
                    ["paymentLink"] = "https://checkout.example/pay/123",
                    ["checkoutId"] = "purchase-123",
                    ["pix"] = new JsonDocument
                    {
                        ["qrCodeText"] = "checkout-pix-code",
                        ["merchantName"] = "Checkout Store",
                        ["pixKey"] = "12345678000195"
                    }
                });

            await _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var interactive = Assert.IsType<JsonDocument>(component["interactive"]);
            var paymentSetting = GetFirstPaymentSetting(GetOrderParameters(interactive));
            var pix = Assert.IsType<JsonDocument>(paymentSetting["pix_dynamic_code"]);
            Assert.Equal("checkout-pix-code", pix["code"]);
            Assert.Equal("Checkout Store", pix["merchant_name"]);
            Assert.Equal("12345678000195", pix["key"]);
            Assert.Equal("CNPJ", pix["key_type"]);
            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync(
                Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>()
            );
            await _checkoutExtension.DidNotReceive().GetCustomerLastCardAsync(
                Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>()
            );
        }

        [Theory]
        [InlineData("Pagar com cartao salvo")]
        [InlineData("checkout:another-purchase:saved-card")]
        [InlineData("Cartão final 9999")]
        [InlineData("Cartão final")]
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
            context.Input.Message.Content = new JsonDocument { ["id"] = "PAY_ONE_CLICK_card-1" };
            var responseSettings = new CheckoutSettings { ProcessSavedCardResponse = true };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None));
            await _checkoutAction.ExecuteAsync(context, responseSettings, CancellationToken.None);

            await _checkoutExtension.Received(1).PayWithSavedCardAsync(Arg.Any<SavedCardPaymentRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("paymentLink", "pix", "payment_link", "pix_dynamic_code")]
        [InlineData("paymentLink", "bankSlip", "payment_link", "boleto")]
        [InlineData("pix", "bankSlip", "pix_dynamic_code", "boleto")]
        public async Task ExecuteAsync_OwnProviderTwoMethods_SendsOneComponent(
            string firstMethod, string secondMethod, string firstType, string secondType)
        {
            var settings = CreateOwnProviderSettings(firstMethod);
            settings.PaymentMethods = new[] { firstMethod, secondMethod }.Select(method => new PaymentMethodSettings
            {
                PaymentMethod = method,
                Provider = "ownProvider",
                PaymentLink = "https://merchant.example/pay/123",
                BusinessName = "Example Store",
                PixKeyType = "email",
                PixKey = "payments@example.com",
                PixCode = "pix-code",
                BankSlipCode = "12345"
            }).ToArray();
            settings.Validate();

            await _checkoutAction.ExecuteAsync(CreateContext(), settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var parameters = GetOrderParameters(Assert.IsType<JsonDocument>(component["interactive"]));
            var payments = Assert.IsType<object[]>(parameters["payment_settings"]);
            Assert.Equal(2, payments.Length);
            Assert.Equal(firstType, Assert.IsType<JsonDocument>(payments[0])["type"]);
            Assert.Equal(secondType, Assert.IsType<JsonDocument>(payments[1])["type"]);
            await _sender.Received(1).SendMessageAsync(Arg.Any<Message>(), CancellationToken.None);
            await AssertCheckoutWasNotCalledAsync();
        }

        [Fact]
        public async Task ExecuteAsync_CheckoutTwoMethods_UsesSeparateSettingsWithoutSavedCardPrompt()
        {
            var settings = CreateSettings();
            var linkSettings = CreateSettings();
            linkSettings.Merchant = "link-merchant";
            var pixSettings = CreateSettings();
            pixSettings.PaymentMethod = "pix";
            pixSettings.Merchant = "pix-merchant";
            pixSettings.Customer = new CustomerSettings { Identity = "pix-customer@wa.gw.msging.net" };
            pixSettings.Source = "PixSource";
            pixSettings.BotShortName = "pix-bot";
            settings.PaymentMethods = new PaymentMethodSettings[] { linkSettings, pixSettings };
            settings.Validate();
            ConfigureSavedCard();
            _checkoutExtension.CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument
                {
                    ["checkoutId"] = "purchase-1",
                    ["paymentLink"] = "https://payment.example/checkout",
                    ["pix"] = new JsonDocument
                    {
                        ["qrCodeText"] = "pix-code",
                        ["merchantName"] = "Example Store",
                        ["pixKey"] = "12345678000195"
                    }
                });

            await _checkoutAction.ExecuteAsync(CreateContext(), settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var parameters = GetOrderParameters(Assert.IsType<JsonDocument>(component["interactive"]));
            var payments = Assert.IsType<object[]>(parameters["payment_settings"]);
            Assert.Equal(2, payments.Length);
            Assert.Equal("https://payment.example/checkout",
                Assert.IsType<JsonDocument>(Assert.IsType<JsonDocument>(payments[0])["payment_link"])["uri"]);
            Assert.Equal("pix-code",
                Assert.IsType<JsonDocument>(Assert.IsType<JsonDocument>(payments[1])["pix_dynamic_code"])["code"]);
            await _checkoutExtension.Received(2).CreateCheckOutLinkAsync(
                Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), CancellationToken.None);
            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync("link-merchant",
                Arg.Is<PaymentCheckoutRequest>(request => request.Customer.Identity == linkSettings.Customer.Identity
                    && request.Source == linkSettings.Source && request.BotShortName == linkSettings.BotShortName), CancellationToken.None);
            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync("pix-merchant",
                Arg.Is<PaymentCheckoutRequest>(request => request.Customer.Identity == pixSettings.Customer.Identity
                    && request.Source == "PixSource" && request.BotShortName == "pix-bot"), CancellationToken.None);
            await _checkoutExtension.DidNotReceive().GetCustomerLastCardAsync(
                Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>());
            await _sender.Received(1).SendMessageAsync(Arg.Any<Message>(), CancellationToken.None);
        }

        [Theory]
        [InlineData("")]
        [InlineData("pix,pix")]
        [InlineData("paymentLink,pix,bankSlip")]
        [InlineData("pix,unknown")]
        public void CheckoutSettings_RejectsInvalidPaymentMethods(string methods)
        {
            var settings = CreateSettings();
            settings.PaymentMethods = methods.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(method => new PaymentMethodSettings { PaymentMethod = method }).ToArray();

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        [Fact]
        public void CheckoutSettings_TwoMethods_ValidatesBothOwnProviderRequirements()
        {
            var settings = CreateOwnProviderSettings("paymentLink");
            settings.PaymentMethods = new[]
            {
                new PaymentMethodSettings { PaymentMethod = "paymentLink", Provider = "ownProvider", PaymentLink = "https://merchant.example/pay/123" },
                new PaymentMethodSettings { PaymentMethod = "pix", Provider = "ownProvider" }
            };

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        [Fact]
        public async Task ExecuteAsync_TwoMethodsOutsideWhatsApp_RejectsWithoutSending()
        {
            var settings = CreateSettings();
            settings.Channel = "Other";
            var pixSettings = CreateSettings();
            pixSettings.PaymentMethod = "pix";
            settings.PaymentMethods = new PaymentMethodSettings[] { CreateSettings(), pixSettings };
            var context = CreateContext();
            context.Input.Message.From = Node.Parse("customer@example.com");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _checkoutAction.ExecuteAsync(context, settings, CancellationToken.None));

            await AssertCheckoutWasNotCalledAsync();
            await _sender.DidNotReceive().SendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_MixedProviders_KeepsPaymentDataSeparate(bool reverseOrder)
        {
            var settings = CreateSettings();
            var link = CreateSettings();
            link.Merchant = "checkout-merchant";
            var pix = new PaymentMethodSettings
            {
                PaymentMethod = "pix",
                Provider = "ownProvider",
                BusinessName = "Own Store",
                PixKeyType = "email",
                PixKey = "own@example.com",
                PixCode = "own-pix-code"
            };
            settings.PaymentMethods = reverseOrder
                ? new PaymentMethodSettings[] { pix, link }
                : new PaymentMethodSettings[] { link, pix };
            settings.Validate();
            _checkoutExtension.CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument
                {
                    ["checkoutId"] = "checkout-purchase",
                    ["paymentLink"] = "https://checkout.example/pay",
                    ["pix"] = new JsonDocument { ["qrCodeText"] = "must-not-be-used" }
                });

            await _checkoutAction.ExecuteAsync(CreateContext(), settings, CancellationToken.None);

            var component = Assert.IsType<JsonDocument>(GetLastMessage().Content);
            var parameters = GetOrderParameters(Assert.IsType<JsonDocument>(component["interactive"]));
            var payments = Assert.IsType<object[]>(parameters["payment_settings"]);
            Assert.Equal(2, payments.Length);
            var linkPayment = Assert.IsType<JsonDocument>(payments[reverseOrder ? 1 : 0]);
            Assert.Equal("https://checkout.example/pay", Assert.IsType<JsonDocument>(linkPayment["payment_link"])["uri"]);
            var pixPayment = Assert.IsType<JsonDocument>(payments[reverseOrder ? 0 : 1]);
            var pixData = Assert.IsType<JsonDocument>(pixPayment["pix_dynamic_code"]);
            Assert.Equal("own-pix-code", pixData["code"]);
            Assert.Equal("Own Store", pixData["merchant_name"]);
            Assert.Equal("own@example.com", pixData["key"]);
            Assert.Equal("EMAIL", pixData["key_type"]);
            Assert.Equal("checkout-purchase", parameters["reference_id"]);
            await _checkoutExtension.Received(1).CreateCheckOutLinkAsync(
                "checkout-merchant", Arg.Any<PaymentCheckoutRequest>(), CancellationToken.None);
            await _checkoutExtension.DidNotReceive().GetCustomerLastCardAsync(
                Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>());
            await _sender.Received(1).SendMessageAsync(Arg.Any<Message>(), CancellationToken.None);
        }

        [Fact]
                public void CheckoutSettings_DeserializesIndependentMethodObjects()
                {
                        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CheckoutSettings>(@"{
                                'channel': 'WhatsApp', 'currency': 'BRL', 'message': 'Payment',
                                'products': [{ 'description': 'Product', 'price': 10, 'quantity': 1 }],
                                'paymentMethods': [
                                        { 'paymentMethod': 'paymentLink', 'provider': 'checkout',
                                            'merchant': 'merchant', 'source': 'Test', 'botShortName': 'bot',
                                            'customer': { 'identity': 'customer@wa.gw.msging.net' } },
                                        { 'paymentMethod': 'pix', 'provider': 'ownProvider',
                                            'businessName': 'Own Store', 'pixKeyType': 'email',
                                            'pixKey': 'own@example.com', 'pixCode': 'own-pix-code' }
                                ]
                        }");

                        settings.Validate();
                        var methods = settings.GetPaymentMethods();
                        Assert.Equal("checkout", methods[0].Provider);
                        Assert.Equal("merchant", methods[0].Merchant);
                        Assert.Equal("ownProvider", methods[1].Provider);
                        Assert.Equal("own-pix-code", methods[1].PixCode);
                        Assert.Null(methods[1].Merchant);
                        Assert.Null(methods[0].PixCode);
                }

                [Fact]
        public void CheckoutSettings_MethodObjects_DoNotInheritTopLevelProviderData()
        {
            var settings = CreateSettings();
            settings.PaymentMethods = new[] { new PaymentMethodSettings { PaymentMethod = "paymentLink", Provider = "checkout" } };

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        [Fact]
        public void CheckoutSettings_RejectsNullMethodObject()
        {
            var settings = CreateSettings();
            settings.PaymentMethods = new PaymentMethodSettings[] { null };

            Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(settings.Validate);
        }

        private void ConfigureSavedCard()
        {
            _checkoutExtension.CreateCheckOutLinkAsync(Arg.Any<string>(), Arg.Any<PaymentCheckoutRequest>(), Arg.Any<CancellationToken>())
                .Returns(new JsonDocument { ["paymentLink"] = "https://payment.example/checkout", ["checkoutId"] = "purchase-1" });
            _checkoutExtension.GetCustomerLastCardAsync(Arg.Any<CustomerLastCardRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CustomerLastCardResult { Success = true, HasCard = true, Id = "card-1", LastFourDigits = "1234" });
        }

        private Message GetLastMessage() => _sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISender.SendMessageAsync))
            .Select(call => (Message)call.GetArguments()[0]).Last();

        private static CheckoutSettings CreateOwnProviderSettings(string paymentMethod)
        {
            var settings = CreateSettings();
            settings.Provider = CheckoutSettings.PROVIDER_OWN;
            settings.PaymentMethod = paymentMethod;
            settings.Merchant = null;
            settings.Customer = null;
            settings.Source = null;
            settings.BotShortName = null;
            return settings;
        }

        private static JsonDocument GetOrderParameters(JsonDocument interactive)
        {
            var action = Assert.IsType<JsonDocument>(interactive["action"]);
            return Assert.IsType<JsonDocument>(action["parameters"]);
        }

        private static JsonDocument GetFirstPaymentSetting(JsonDocument parameters)
        {
            var paymentSettings = Assert.IsType<object[]>(parameters["payment_settings"]);
            return Assert.IsType<JsonDocument>(paymentSettings[0]);
        }

        private async Task AssertCheckoutWasNotCalledAsync()
        {
            await _checkoutExtension.DidNotReceive().CreateCheckOutLinkAsync(
                Arg.Any<string>(),
                Arg.Any<PaymentCheckoutRequest>(),
                Arg.Any<CancellationToken>()
            );
        }

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