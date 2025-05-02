using System.Text.Json;
namespace Telegram.Bot.Services
{
    public interface ITranslationService
    {
        string Get(string language, string key, params object[] args);
    }

    public class JsonTranslationService : ITranslationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _translations = new();
        private const string DefaultLanguage = "EN";

        public JsonTranslationService(string resourceFolder)
        {
            foreach (var file in Directory.GetFiles(resourceFolder, "*.json"))
            {
                var lang = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                var content = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                if (dict != null)
                    _translations[lang] = dict;
            }

            if (!_translations.ContainsKey(DefaultLanguage))
                throw new InvalidOperationException($"Missing default language file: {DefaultLanguage.ToLower()}.json");
        }

        public string Get(string language, string key, params object[] args)
        {
            language = language?.ToUpperInvariant() ?? DefaultLanguage;

            if (_translations.TryGetValue(language, out var langDict) &&
                langDict.TryGetValue(key, out var message))
            {
                return string.Format(message, args);
            }

            if (_translations[DefaultLanguage].TryGetValue(key, out var fallback))
            {
                return string.Format(fallback, args);
            }


            return $"[{key}]";
        }
    }
}