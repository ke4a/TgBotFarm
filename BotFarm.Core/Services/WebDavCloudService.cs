using BotFarm.Core.Models;
using BotFarm.Core.Services.Interfaces;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using WebDav;

namespace BotFarm.Core.Services
{
    public class WebDavCloudService : ICloudService
    {
        private readonly ILogger<WebDavCloudService> _logger;
        private readonly INotificationService _notificationService;
        private readonly WebDavClientParams clientParams;
        private readonly IEnumerable<BotConfig> _botConfigs;
        private readonly string tempPath;
        private readonly string remoteRoot;

        private const string logPrefix = $"[{nameof(WebDavCloudService)}]";

        public WebDavCloudService(
            ILogger<WebDavCloudService> logger,
            INotificationService notificationService,
            IOptions<WebDAVSettings> webDavConfig,
            IEnumerable<IOptions<BotConfig>> botConfigs)
        {
            _logger = logger;
            _notificationService = notificationService;
            tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
            clientParams = new WebDavClientParams
            {
                BaseAddress = new Uri(webDavConfig.Value.Address),
                Credentials = new NetworkCredential(webDavConfig.Value.Login, webDavConfig.Value.Password)
            };
            remoteRoot = $"{webDavConfig.Value.Address}/{webDavConfig.Value.Folder}";
            _botConfigs = botConfigs.Select(c => c.Value);
        }

        public async Task<bool> Upload(string path, string botName)
        {
            var handle = _botConfigs.First(c => c.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase)).Handle;
            using (var fs = File.OpenRead(path))
            using (var client = new WebDavClient(clientParams))
            {
                _logger.LogInformation($"{logPrefix} Uploading '{path}' file to remote folder '{remoteRoot}/{handle}'.");
                var result = await client.PutFile($"{remoteRoot}/{handle}/{Path.GetFileName(path)}", fs);
                if (result.IsSuccessful)
                {
                    _logger.LogInformation($"{logPrefix} File '{path}' uploaded successfully.");
                }
                else
                {
                    var message = $"{logPrefix} Failed to upload file '{path}'. Response status code: '{result.StatusCode}'. Response message: '{result.Description}'.";
                    _logger.LogError(message);
                    await _notificationService.SendErrorNotification(message, botName);
                }

                return result.IsSuccessful;
            }
        }

        public async Task<bool> CleanupRemote(string botName)
        {
            var handle = _botConfigs.First(c => c.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase)).Handle;
            using (var client = new WebDavClient(clientParams))
            {
                var result = await client.Propfind($"{remoteRoot}/{handle}");
                if (result.Resources.Count > 8) // 7 backups + root folder
                {
                    var oldest = result.Resources
                        .OrderByDescending(r => r.CreationDate)
                        .Last(r => !r.IsCollection); // just in case last is root folder (should not happen)
                    _logger.LogInformation($"{logPrefix} Removing oldest backup '{oldest.DisplayName}' from remote folder '{remoteRoot}/{handle}'.");
                    var deleteResult = await client.Delete(oldest.Uri);
                    if (deleteResult.IsSuccessful)
                    {
                        _logger.LogInformation($"{logPrefix} Successfully removed file '{oldest.DisplayName}'.");
                    }
                    else
                    {
                        var message = $"{logPrefix} Failed to delete file '{oldest.DisplayName}'. Response status code: '{deleteResult.StatusCode}'. Response message: '{deleteResult.Description}'.";
                        _logger.LogError(message);
                        await _notificationService.SendErrorNotification(message, botName);
                    }
                }
                else
                {
                    _logger.LogInformation($"{logPrefix} Nothing to clean up in remote folder '{remoteRoot}/{handle}'.");
                }

                return result.IsSuccessful;
            }
        }

        public async Task<string> DownloadBackup(string uri, string botName)
        {
            _logger.LogInformation($"{logPrefix} Downloading '{uri}' backup to '{tempPath}' folder.");
            var localBackupPath = Path.Combine(tempPath, Path.GetFileName(uri));
            Directory.CreateDirectory(tempPath);

            using (var client = new WebDavClient(clientParams))
            using (var fs = File.Create(localBackupPath))
            using (var response = await client.GetRawFile($"{uri}"))
            {
                if (response.IsSuccessful)
                {
                    response.Stream.CopyTo(fs);
                    fs.Flush();
                    _logger.LogInformation($"{logPrefix} Backup '{uri}' downloaded successfully.");
                    return localBackupPath;
                }
                var message = $"{logPrefix} Failed to download backup '{uri}'. Response status code: '{response.StatusCode}'. Response message: '{response.Description}'.";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, botName);
                return string.Empty;
            }
        }

        public async Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string botName)
        {
            var handle = _botConfigs.First(c => c.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase)).Handle;
            var backupsList = new List<BackupInfo>();
            using (var client = new WebDavClient(clientParams))
            {
                _logger.LogInformation($"{logPrefix} Getting backups list from remote folder '{remoteRoot}/{handle}'.");
                var result = await client.Propfind($"{remoteRoot}/{handle}");
                if (result.IsSuccessful)
                {
                    var backups = result.Resources
                        .Where(r => !r.IsCollection) // skip root folder
                        .OrderByDescending(r => r.CreationDate);
                    foreach (var backup in backups)
                    {
                        var date = backup.CreationDate ?? DateTime.ParseExact(backup.DisplayName.Replace(".zip", string.Empty), "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                        backupsList.Add(new BackupInfo
                        {
                            Date = date,
                            Name = backup.DisplayName,
                            Size = backup.ContentLength,
                            Link = $"{remoteRoot}/{handle}/{backup.DisplayName}",
                            Uri = backup.Uri,
                        });
                    }

                    var successMessage = $"{logPrefix} Found {backupsList.Count} backups in remote folder '{remoteRoot}/{handle}'.";
                    _logger.LogInformation(successMessage);
                    return new Result<IEnumerable<BackupInfo>>().WithSuccess(successMessage).WithValue(backupsList);
                }

                var failMessage = $"{logPrefix} Could not retrieve backups list from remote folder '{remoteRoot}/{handle}'. Error: {result.Description}";
                _logger.LogError(failMessage);
                await _notificationService.SendErrorNotification(failMessage, botName);
                return new Result<IEnumerable<BackupInfo>>().WithError(failMessage);
            }
        }

        public async Task<Result> RemoveBackup(string backupName, string botName)
        {
            var handle = _botConfigs.First(c => c.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase)).Handle;
            var backups = await GetBackupsList(botName);
            var backup = backups.ValueOrDefault.FirstOrDefault(b => b.Name.Equals(backupName, StringComparison.InvariantCultureIgnoreCase));
            if (backup != null)
            {
                using (var client = new WebDavClient(clientParams))
                {
                    _logger.LogInformation($"{logPrefix} Removing backup '{backup.Name}' from remote folder '{remoteRoot}/{handle}'.");
                    var deleteResult = await client.Delete(backup.Uri);
                    if (deleteResult.IsSuccessful)
                    {
                        var successMessage = $"{logPrefix} Successfully removed file '{backup.Name}'.";
                        _logger.LogInformation(successMessage);
                        return new Result().WithSuccess(successMessage);
                    }
                    else
                    {
                        var failMessage = $"{logPrefix} Failed to delete file '{backup.Name}'. Response status code: '{deleteResult.StatusCode}'. Response message: '{deleteResult.Description}'.";
                        _logger.LogError(failMessage);
                        await _notificationService.SendErrorNotification(failMessage, botName);
                        return new Result().WithError(failMessage);
                    }
                }
            }
            else
            {
                var failMessage = $"{logPrefix} Backup '{backupName}' not found in remote folder '{remoteRoot}/{handle}'.";
                _logger.LogError(failMessage);
                return new Result().WithError(failMessage);
            }
        }
    }
}
