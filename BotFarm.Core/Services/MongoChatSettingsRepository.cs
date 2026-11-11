using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Runtime.CompilerServices;

using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services;

/// <summary>
/// Owns caching and CRUD for <see cref="ChatSettings"/> (and derived types)
/// against the <c>ChatSettings</c> collection.
/// </summary>
internal sealed class MongoChatSettingsRepository
{
    private readonly Func<IMongoDatabase> _getInstance;
    private readonly HybridCache _cache;
    private readonly string _name;

    public MongoChatSettingsRepository(Func<IMongoDatabase> getInstance, HybridCache cache, string name)
    {
        _getInstance = getInstance;
        _cache = cache;
        _name = name;
    }

    public async Task<IEnumerable<long>> GetAllChatIds()
    {
        var ids = await _cache.GetOrCreateAsync(
            $"{_name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}",
            async (cancel) =>
            {
                var collection = _getInstance().GetCollection<ChatSettings>(nameof(ChatSettings));

                return await collection.Find(Builders<ChatSettings>.Filter.Empty)
                                        .Project(settings => settings.ChatId)
                                        .ToListAsync(cancellationToken: cancel);
            },
            tags: [_name, nameof(ChatSettings)]
        );

        return ids;
    }

    public Task<long> GetChatCount(bool estimated = true)
    {
        var collection = _getInstance().GetCollection<ChatSettings>(nameof(ChatSettings));
        return collection.GetDocumentCountAsync(estimated);
    }

    public async Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings
    {
        var settings = await GetChatSettings<TSettings>(chatId);

        var language = settings?.Language;
        if (string.IsNullOrWhiteSpace(language))
        {
            await SetChatLanguage<TSettings>(chatId, Constants.DefaultLanguage);
            return Constants.DefaultLanguage;
        }

        return language;
    }

    public async Task SetChatLanguage<TSettings>(long chatId, string language) where TSettings : ChatSettings
    {
        var update = Builders<TSettings>.Update.Set(x => x.Language, language);
        _ = await UpdateChatSettings(chatId, update);
    }

    public async Task<TSettings> SaveChatSettings<TSettings>(TSettings settings) where TSettings : ChatSettings
    {
        var collection = _getInstance().GetCollection<TSettings>(nameof(ChatSettings));
        var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, settings.ChatId);
        var options = new FindOneAndReplaceOptions<TSettings>()
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };
        var updatedSettings = await collection.FindOneAndReplaceAsync(filter, settings, options);

        await _cache.SetAsync(
            $"{_name}|{nameof(ChatSettings)}|{settings.ChatId}",
            updatedSettings,
            tags: [_name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");

        return updatedSettings;
    }

    public async Task<TSettings> UpdateChatSettings<TSettings>(long chatId, UpdateDefinition<TSettings> update) where TSettings : ChatSettings
    {
        var collection = _getInstance().GetCollection<TSettings>(nameof(ChatSettings));
        var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, chatId);
        var options = new FindOneAndUpdateOptions<TSettings>()
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };
        var updatedSettings = await collection.FindOneAndUpdateAsync(filter, update, options);

        await _cache.SetAsync(
            $"{_name}|{nameof(ChatSettings)}|{chatId}",
            updatedSettings,
            tags: [_name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        var allIds = await GetAllChatIds();
        if (!allIds.Contains(updatedSettings.ChatId))
        {
            await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");
        }

        return updatedSettings;
    }

    public async Task<TSettings?> GetChatSettings<TSettings>(long chatId) where TSettings : ChatSettings
    {
        var settings = await _cache.GetOrCreateAsync(
            $"{_name}|{nameof(ChatSettings)}|{chatId}",
            async cancel =>
            {
                var collection = _getInstance().GetCollection<TSettings>(nameof(ChatSettings));
                var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, chatId);

                return await collection.Find(filter).FirstOrDefaultAsync(cancel);
            },
            tags: [_name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        return settings;
    }

    /// <summary>
    /// Moves the settings document stored under <paramref name="fromChatId"/> to <paramref name="toChatId"/>.
    /// The document is handled as raw BSON so fields of derived settings types are preserved. Settings already
    /// stored under <paramref name="toChatId"/> are kept, and the source document is removed in either case.
    /// Safe to repeat: the copy is written before the source is deleted, so an interrupted move finishes on the
    /// next call.
    /// </summary>
    public async Task<ChatSettingsMoveResult> MoveChatSettings(long fromChatId, long toChatId)
    {
        var collection = _getInstance().GetCollection<BsonDocument>(nameof(ChatSettings));
        var sourceFilter = Builders<BsonDocument>.Filter.Eq("_id", fromChatId);

        var source = await collection.Find(sourceFilter).FirstOrDefaultAsync();
        if (source == null)
        {
            await InvalidateMovedChatCache(fromChatId, toChatId);
            return ChatSettingsMoveResult.NotFound;
        }

        var target = source.DeepClone().AsBsonDocument;
        target["_id"] = toChatId;

        var result = ChatSettingsMoveResult.Moved;
        try
        {
            await collection.InsertOneAsync(target);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            result = ChatSettingsMoveResult.KeptExisting;
        }

        await collection.DeleteOneAsync(sourceFilter);
        await InvalidateMovedChatCache(fromChatId, toChatId);

        return result;
    }

    /// <summary>
    /// Deletes the settings document of <paramref name="chatId"/> and removes it from the cache.
    /// Returns <see langword="false"/> when the chat had no settings document.
    /// </summary>
    public async Task<bool> DeleteChatSettings(long chatId)
    {
        var collection = _getInstance().GetCollection<BsonDocument>(nameof(ChatSettings));
        var result = await collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", chatId));

        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{chatId}");
        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");

        return result.DeletedCount > 0;
    }

    private async Task InvalidateMovedChatCache(long fromChatId, long toChatId)
    {
        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{fromChatId}");
        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{toChatId}");
        await _cache.RemoveAsync($"{_name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");
    }

    public async IAsyncEnumerable<TSettings> GetAllChatSettings<TSettings>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where TSettings : ChatSettings
    {
        var collection = _getInstance().GetCollection<TSettings>(nameof(ChatSettings));
        using var cursor = await collection.Find(Builders<TSettings>.Filter.Empty)
            .ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var chat in cursor.Current)
            {
                await _cache.SetAsync(
                    $"{_name}|{nameof(ChatSettings)}|{chat.ChatId}",
                    chat,
                    tags: [_name, typeof(TSettings).Name, nameof(ChatSettings)]
                );

                yield return chat;
            }
        }
    }
}
