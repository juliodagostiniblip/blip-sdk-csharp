using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lime.Protocol;
using Take.Blip.Builder.Actions.Checkout.Settings;
using Take.Blip.Client;
using Lime.Messaging.Contents;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Responsible for building and sending the checkout related messages to the customer.
    /// </summary>
    internal class CheckoutMessageSender
    {
        private readonly ISender _sender;

        public CheckoutMessageSender(ISender sender)
        {
            _sender = sender;
        }

        public async Task SendCheckoutLinkAsync(IContext context, CheckoutSettings settings, Document checkoutLinkDocument, string checkoutLink, CancellationToken cancellationToken)
        {
            var content = MetaOrderComponentFactory.ShouldSend(context, settings)
                    ? MetaOrderComponentFactory.Create(settings, checkoutLinkDocument, checkoutLink)
                    : (Document)new WebLink
                    {
                        Text = settings.Message,
                        Uri = new Uri(checkoutLink)
                    };

            await SendMessageAsync(context, content, cancellationToken);
        }

        public Task SendSavedCardOptionsAsync(IContext context, PendingCheckout pending, string lastFourDigits, CancellationToken cancellationToken)
        {
            return SendMessageAsync(context, new Select
            {
                Text = $"Deseja pagar com o cartao salvo de final {lastFourDigits}?",
                Options = new[]
                {
                    new SelectOption { Text = "Pagar com cartao salvo", Value = new PlainText { Text = $"checkout:{pending.ChoiceId}:saved-card" } },
                    new SelectOption { Text = "Pagar de outra forma", Value = new PlainText { Text = $"checkout:{pending.ChoiceId}:other" } }
                }
            }, cancellationToken);
        }

        private async Task SendMessageAsync(IContext context, Document content, CancellationToken cancellationToken)
        {
            var message = new Message
            {
                Id = EnvelopeId.NewId(),
                To = context.Input.Message.From,
                Content = content
            };

            if (context.Input.Message.From.Domain.Equals("tunnel.msging.net"))
            {
                message.Metadata ??= new Dictionary<string, string>();

                if (context.Input.Message.Metadata?.TryGetValue("#tunnel.owner", out string owner) == true)
                {
                    message.Metadata.Add("#tunnel.owner", owner);
                }

                if (context.Input.Message.Metadata?.TryGetValue("#tunnel.originator", out string originator) == true)
                {
                    message.Metadata.Add("#tunnel.originator", originator);
                }
            }

            await _sender.SendMessageAsync(message, cancellationToken);
        }
    }
}
