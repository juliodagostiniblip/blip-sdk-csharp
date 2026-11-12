using System;
using Lime.Messaging.Contents;
using Lime.Protocol;

namespace Take.Blip.Builder.Actions.Checkout
{
    /// <summary>
    /// Extracts checkout link and values from the documents returned by the checkout extension.
    /// </summary>
    internal static class CheckoutLinkResolver
    {
        public static string GetCheckoutLink(Document checkoutLinkDocument)
        {
            if (checkoutLinkDocument is WebLink webLink && webLink.Uri != null)
            {
                return webLink.Uri.ToString();
            }

            if (checkoutLinkDocument is MediaLink mediaLink && mediaLink.Uri != null)
            {
                return mediaLink.Uri.ToString();
            }

            if (checkoutLinkDocument is PlainText plainText && !string.IsNullOrWhiteSpace(plainText.Text))
            {
                if (Uri.TryCreate(plainText.Text, UriKind.Absolute, out var plainTextUri))
                {
                    return plainTextUri.ToString();
                }
            }

            if (checkoutLinkDocument is DictionaryDocument<string, object> dictionaryDocument)
            {
                if (TryGetUrlFromDictionary(dictionaryDocument, "paymentLink", out var paymentLink))
                {
                    return paymentLink;
                }

                if (TryGetUrlFromDictionary(dictionaryDocument, "uri", out var uri))
                {
                    return uri;
                }

                if (TryGetUrlFromDictionary(dictionaryDocument, "url", out var url))
                {
                    return url;
                }

                if (dictionaryDocument.TryGetValue("errorMessage", out var errorMessage)
                    && errorMessage is string errorMessageText
                    && !string.IsNullOrWhiteSpace(errorMessageText))
                {
                    throw new InvalidOperationException($"Checkout link could not be created: {errorMessageText}");
                }
            }

            if (checkoutLinkDocument != null)
            {
                var fallback = checkoutLinkDocument.ToString();
                if (Uri.TryCreate(fallback, UriKind.Absolute, out var fallbackUri))
                {
                    return fallbackUri.ToString();
                }

                throw new InvalidOperationException($"Invalid checkout link document content '{fallback}' from type '{checkoutLinkDocument.GetType().Name}'.");
            }

            throw new InvalidOperationException("Checkout link document cannot be null.");
        }

        public static string TryGetDictionaryValueAsString(DictionaryDocument<string, object> document, string key)
        {
            if (document == null || !document.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            var valueText = value as string ?? value.ToString();
            return string.IsNullOrWhiteSpace(valueText) ? null : valueText;
        }

        private static bool TryGetUrlFromDictionary(DictionaryDocument<string, object> document, string key, out string absoluteUrl)
        {
            absoluteUrl = null;

            if (!document.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            var valueText = value as string ?? value.ToString();
            if (string.IsNullOrWhiteSpace(valueText))
            {
                return false;
            }

            if (!Uri.TryCreate(valueText, UriKind.Absolute, out var valueUri))
            {
                return false;
            }

            absoluteUrl = valueUri.ToString();
            return true;
        }
    }
}
