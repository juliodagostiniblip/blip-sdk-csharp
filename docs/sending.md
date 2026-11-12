### Send

In order to send messages and notifications use an instance of `ISender`, wich is automaticaly injected on constructors of registered `receivers` defined on project and on `Startup` class.

The sample bellow show how to reply a received message:

```csharp
public class PlainTextMessageReceiver : IMessageReceiver
{
    private readonly ISender _sender;

    public PlainTextMessageReceiver(IMessagingHubSender sender)
    {
        _sender = sender;
    }

    public async Task ReceiveAsync(Message message, CancellationToken cancellationToken)
    {
        // Write the received message to the console
        Console.WriteLine(message.Content.ToString());
        // Responds to the received message
        _sender.SendMessageAsync("Hi. I just received your message!", message.From, cancellationToken);
    }
}
```

The process of send message is asynchronous and the status of sent messages is delivered to application by **notifications**.

`ISender` interface also enable send **commands** to the server, as the follow sample:

```csharp
var command = new Command {
    Method = CommandMethod.Get,
    Uri = new LimeUri("/account")
};

var response = await _sender.ProcessCommandAsync(command, cancellationToken);
```
For this case, the command response is received on a synchronous way.

### Builder Checkout With a Saved Card

Configure the `Checkout` action in a state's `inputActions` with the customer,
products, merchant and bot settings. The link creation response must include
`purchaseId` alongside `paymentLink` (or `uri`/`url`) for saved-card lookup.
Responses without `purchaseId` continue with the existing payment message.

When a saved card is available, the action sends a selection with the card's
last four digits and options to pay with that card or another method. Otherwise,
it sends the payment methods immediately (`order_details` on WhatsApp).
Card lookup failures also fall back to the existing payment message.

The state must wait for input (`"input": { "bypass": false }`). Add a second
`Checkout` action to its `outputActions` to process the selection:

```json
{
    "type": "Checkout",
    "settings": {
        "processSavedCardResponse": true,
        "pendingCheckoutVariable": "checkout.pending"
    }
}
```

Use the same `pendingCheckoutVariable` in both actions; its default is
`checkout.pending`. Use distinct names for independent checkouts. The pending
choice expires after 30 minutes. Selection values are correlated with the
pending purchase; labels, unrelated messages and expired choices never trigger
a payment. Configure state transitions to keep invalid responses in the input
state, and route a completed choice to the appropriate payment follow-up state.

The response action reuses the original purchase and link. Choosing another
method sends the original payment message. Choosing the saved card sends
`/iris/payment/saved-card` with the stored `cardId` and one installment.
It does not announce payment approval; retain the flow's payment-status handling.
The pending choice is consumed before requesting payment to avoid automatic
retries after an uncertain result. Command failures propagate through the
Builder's action error handling and must be reconciled before a new attempt.
