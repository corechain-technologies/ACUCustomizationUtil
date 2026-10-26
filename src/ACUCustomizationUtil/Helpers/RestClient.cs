using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

using ACUCustomizationUtils.Common;
using ACUCustomizationUtils.Configuration.ACU;

using static ACUCustomizationUtils.Common.Messages;

using Request = ACUCustomizationUtils.Helpers.RestModel.Request;
using Response = ACUCustomizationUtils.Helpers.RestModel.Response;

namespace ACUCustomizationUtils.Helpers
{
    internal class RestClient : IAcuCustomizationClient
    {
        private readonly string? _packageName;
        private readonly string? _packageDirectory;
        private readonly HttpClient _client;

        public RestClient(IAcuConfiguration configuration)
        {
            Uri baseAddress = configuration.Pkg.Url!;
            string username = configuration.Pkg.Login!;
            string password = configuration.Pkg.Password!;
            string? tenant = configuration.Pkg.Tenant;
            string? branch = configuration.Pkg.Branch;
            _packageName = configuration.Pkg.PkgName;
            _packageDirectory = configuration.Pkg.PkgDirectory;
            HttpClientHandler options = new()
            {
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer(),
            };

            _client = new HttpClient(options)
            {
                BaseAddress = baseAddress,
                DefaultRequestHeaders =
                {
                    Accept = { new MediaTypeWithQualityHeaderValue("application/json") },
                },
            };

            // HttpClient defaults to a 100-second timeout, which a cold or slow
            // instance (site warmup, large project export) can legitimately
            // exceed. Allow configuration to raise it; otherwise keep the default.
            if (configuration.Pkg.Timeout is > 0)
            {
                _client.Timeout = TimeSpan.FromSeconds(configuration.Pkg.Timeout.Value);
            }

            Request.Login login = new()
            {
                Name = username,
                Password = password,
                Tenant = tenant,
                Branch = branch,
                Locale = null,
            };

            Login(login);
        }

        public async Task GetPackage()
        {
            Response.GetProject res = await GetProjectAsync(_packageName!);
            string directory = _packageDirectory!;
            string file = _packageName!.EndsWith(".zip") ? _packageName : $"{_packageName}.zip";
            string filePath = Path.Combine(directory, file);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(
                filePath,
                Convert.FromBase64String(res.ProjectContentBase64)
            );
        }

        public async Task UnpublishAllPackages()
        {
            await UnpublishAllAsync();
        }

        public async Task UploadPackage()
        {
            string packageName = _packageName!;
            string directory = _packageDirectory!;
            string file = _packageName!.EndsWith(".zip") ? _packageName : $"{_packageName}.zip";
            string filePath = Path.Combine(directory, file);
            byte[] packageContents = await File.ReadAllBytesAsync(filePath);
            string projectContentBase64 = Convert.ToBase64String(packageContents);

            await ImportAsync(packageName, projectContentBase64);
        }

        public async Task PublishPackages()
        {
            string[] packageNames = new[] { _packageName ?? string.Empty };
            const bool mergeWithExistingPackages = true;
            bool isPublished = false;

            await PublishBeginAsync(packageNames, mergeWithExistingPackages);
            while (!isPublished)
            {
                await Task.Delay(1000);
                Response.PublishEnd res = await PublishEndAsync();
                isPublished = res.IsCompleted;

                if (res.IsFailed)
                {
                    string msg = string.Empty;
                    foreach (RestModel.Log? log in res.Log!.Where(l => l.LogType == Messages.ErrorLogType))
                    {
                        msg += $"\n{log.Message}";
                    }
                    throw new Exception(msg);
                }
            }
        }

        private async Task<Response.PublishBegin> PublishBeginAsync(
            string[] projectNames,
            bool isMergeWithExistingPackages = false,
            bool isOnlyValidation = false,
            bool isOnlyDbUpdates = false,
            bool isReplayPreviouslyExecutedScripts = false,
            string tenantMode = TenantMode.All
        )
        {
            Request.PublishBegin model = new()
            {
                ProjectNames = projectNames,
                IsMergeWithExistingPackages = isMergeWithExistingPackages,
                IsOnlyValidation = isOnlyValidation,
                IsOnlyDbUpdates = isOnlyDbUpdates,
                IsReplayPreviouslyExecutedScripts = isReplayPreviouslyExecutedScripts,
                TenantMode = tenantMode,
            };
            return await PostAsync<Response.PublishBegin>(APIResource.PublishBegin, model);
        }

        private async Task<Response.UnpublishAll> UnpublishAllAsync(
            string tenantMode = TenantMode.All,
            string[]? tenantLoginNames = null
        )
        {
            Request.UnpublishAll model = new()
            {
                TenantMode = tenantMode,
                TenantLoginNames = tenantLoginNames,
            };
            return await PostAsync<Response.UnpublishAll>(APIResource.UnpublishAll, model);
        }

        private async Task<Response.PublishEnd> PublishEndAsync()
        {
            return await PostAsync<Response.PublishEnd>(APIResource.PublishEnd, null);
        }

        private async Task<Response.Import> ImportAsync(
            string projectName,
            string projectContentBase64,
            int projectLevel = 0,
            bool isReplaceIfExists = true,
            string? projectDescription = null
        )
        {
            Request.Import model = new()
            {
                ProjectLevel = projectLevel,
                IsReplaceIfExists = isReplaceIfExists,
                ProjectName = projectName,
                ProjectDescription = projectDescription,
                ProjectContentBase64 = projectContentBase64,
            };
            return await PostAsync<Response.Import>(APIResource.Import, model);
        }

        private async Task<Response.GetProject> GetProjectAsync(
            string projectName,
            bool isAutoResolveConflicts = true
        )
        {
            Request.GetProject model = new()
            {
                IsAutoResolveConflicts = isAutoResolveConflicts,
                ProjectName = projectName,
            };
            return await PostAsync<Response.GetProject>(APIResource.GetProject, model);
        }

        private void Login(Request.Login model)
        {
            using HttpResponseMessage response = Post(APIResource.Login, model);
            if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
            {
                throw new InvalidCredentialException(
                    $"Error login to service: {response.StatusCode} {response.Content}"
                );
            }
        }

        private void Logout()
        {
            Post(APIResource.Logout, null);
        }

        public void Dispose()
        {
            Logout();
            _client?.Dispose();
            GC.SuppressFinalize(this);
        }

        private HttpResponseMessage Post(string resource, object? model)
        {
            using HttpRequestMessage message = CreateHttpRequestMessage(resource, model);
            return _client.Send(message);
        }

        private static HttpRequestMessage CreateHttpRequestMessage(string resource, object? model)
        {
            HttpRequestMessage message = new(HttpMethod.Post, resource);
            if (model != null)
            {
                message.Content = new StringContent(
                    JsonSerializer.Serialize(model),
                    Encoding.UTF8,
                    "application/json"
                );
            }
            return message;
        }

        private const int MaxServerDetailLength = 1000;

        private async Task<T> PostAsync<T>(string resource, object? model)
            where T : RestModel.Logs
        {
            using HttpResponseMessage response = await _client.PostAsJsonAsync(resource, model);
            // PostAsJsonAsync buffers the response content, so it can be read again as text below.
            try
            {
                // Success rule and decoding unchanged: the response deserializes into the expected model.
                T? result = await response.Content.ReadFromJsonAsync<T>();
                if (result != null)
                    return result;
            }
            catch (JsonException)
            {
                // Fall through: report what the server actually said instead of the model mismatch.
            }

            string body = await response.Content.ReadAsStringAsync();

            throw new HttpRequestException(
                $"{resource} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. "
                    + $"Server: {DescribeServerResponse(body)}",
                null,
                response.StatusCode
            );
        }

        /// <summary>
        /// The server's own explanation of a failed call: the customization API's <c>log</c> entries,
        /// else a standard error body's message fields, else the raw text (an HTML page is
        /// reduced to its title). The result is single-line, control-character free and bounded.
        /// </summary>
        private static string DescribeServerResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "(empty response)";
            if (body.TrimStart().StartsWith('<'))
            {
                string? title = HtmlTitle(body);
                return Bound(title != null ? $"HTML page: {title}" : "(HTML page without title)");
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    List<string> parts = new();
                    if (root.TryGetProperty("log", out JsonElement log) && log.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement entry in log.EnumerateArray())
                            if (entry.ValueKind == JsonValueKind.Object
                                && entry.TryGetProperty("message", out JsonElement message)
                                && message.ValueKind == JsonValueKind.String)
                            {
                                string type = entry.TryGetProperty("logType", out JsonElement t) && t.ValueKind == JsonValueKind.String
                                    ? t.GetString()!
                                    : "log";
                                parts.Add($"[{type}] {message.GetString()}");
                            }
                    foreach (string field in new[] { "message", "exceptionMessage", "error", "title", "detail" })
                        if (root.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                            parts.Add(value.GetString()!);
                    if (parts.Count > 0)
                        return Bound(string.Join(" | ", parts));
                }
            }
            catch (JsonException)
            {
                // Not JSON: fall back to the raw text.
            }

            return Bound(body.Trim());
        }

        /// <summary>First &lt;title&gt; element's text; single forward scan, linear in body length.</summary>
        private static string? HtmlTitle(string body)
        {
            int open = body.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (open < 0)
                return null;
            int start = body.IndexOf('>', open);
            if (start < 0)
                return null;
            int end = body.IndexOf("</title", start + 1, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? null : body[(start + 1)..end];
        }

        private static string Bound(string text)
        {
            // One line, no control characters (no terminal escapes or log-line injection).
            string clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            clean = new string(clean.Where(c => !char.IsControl(c)).ToArray());
            return clean.Length <= MaxServerDetailLength ? clean : clean[..(MaxServerDetailLength - 1)] + "…";
        }
    }
}
