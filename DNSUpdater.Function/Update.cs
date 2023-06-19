using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DNSUpdater.Library.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;

namespace DNSUpdater.Function
{
    public class Update
    {
        private readonly IDnsService service;
        private readonly ILogger<Update> logger;
        private readonly List<string> tokenList;
        private readonly string[] validKeys = new string[] { "hostname", "myip", "myipv6", "system" };
        public Update(IDnsServiceFactory serviceFactory, IConfiguration config, ILogger<Update> logger)
        {
            this.service = serviceFactory.GetDnsServiceAsync();
            this.logger = logger;
            this.tokenList = GetAuthorizedUsers(config);
            if (this.tokenList.Count == 0)
            {
                var raw = config.ToString();
                this.logger.LogError("Failed configuration file: {0}", raw);
                throw new ArgumentOutOfRangeException("Authorization", "Authorization section is empty in configuration file");
            }
        }


        [FunctionName("update")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
            HttpRequest req)
        {
            int ttl = 3600;
            string agent = req.Headers["User-Agent"];
            string hostname = req.Query["hostname"];
            string ipv4 = req.Query["myip"];
            string ipv6 = req.Query["myipv6"];
            string system = req.Query["system"];
            string token = req.Headers["Authorization"]; //For "Basic" authentication the credentials are constructed by first combining the username and the password with a colon (aladdin:opensesame), and then by encoding the resulting string in base64 (YWxhZGRpbjpvcGVuc2VzYW1l).

            foreach (var queryParameter in req.Query)
            {
                if (Array.IndexOf(validKeys,queryParameter.Key) == -1)
                {
                    this.logger.LogWarning($"Query parameter: {queryParameter.Key} is invalid");
                    return new BadRequestResult();
                }
            }

            if (string.IsNullOrWhiteSpace(hostname) ||
                (string.IsNullOrWhiteSpace(ipv4) && string.IsNullOrWhiteSpace(ipv6)) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(agent))
            {
                if (string.IsNullOrWhiteSpace(hostname))
                {
                    logger.LogWarning("Query parameter \"hostname\" is empty.");
                }

                if (string.IsNullOrWhiteSpace(ipv4) && string.IsNullOrWhiteSpace(ipv6))
                {
                    logger.LogWarning("Query parameter \"myip\" and \"myipv6\" are empty.");
                }

                if (string.IsNullOrWhiteSpace(system))
                {
                    logger.LogWarning("Query parameter \"system\" is empty.");
                }

                if (string.IsNullOrWhiteSpace(agent))
                {
                    logger.LogWarning("Request header \"User-Agent\" is empty.");
                }

                return new BadRequestObjectResult(UpdateStatus.othererr.ToString());
            }

            this.logger.LogDebug(
                $"request details:	hostname: {hostname}	ip: {ipv4}	ipv6: {ipv6}	token: {token}	agent: {agent}	system: {system}");

            token = token.Replace("Basic ", "");
            if (!tokenList.Contains(token))
            {
                this.logger.LogWarning($"Unauthorized request. Provided token");
                LogTokensToDebug(token);
                return new ObjectResult(UpdateStatus.badauth.ToString()) { StatusCode = 401 };
            }

            try
            {
                UpdateStatus resulterv4 = UpdateStatus.nochg;
                UpdateStatus resulterv6 = UpdateStatus.nochg;
                if (!string.IsNullOrWhiteSpace(ipv4))
                {
                    logger.LogInformation($"Updating hostname: {hostname}\tip: {ipv4}");
                    var result = await this.service.UpdateARecord(hostname, ipv4, ttl);
                    resulterv4 = result;
                }

                if (!string.IsNullOrWhiteSpace(ipv6))
                {
                    logger.LogInformation($"Updating hostname: {hostname}\tipv6: {ipv6}");
                    var result = await this.service.UpdateAAAARecord(hostname, ipv6, ttl);
                    resulterv6 = result;
                }

                if (resulterv4 == UpdateStatus.good || resulterv6  == UpdateStatus.good)
                {
                    return new OkObjectResult(UpdateStatus.good.ToString());
                } else if (resulterv4 == UpdateStatus.nochg && resulterv6 == UpdateStatus.nochg)
                {
                    return new OkObjectResult(UpdateStatus.nochg.ToString());
                } else if (resulterv4 == UpdateStatus.badauth || resulterv6 == UpdateStatus.badauth)
                {
                    return new ForbidResult(UpdateStatus.badauth.ToString());
                } else if (resulterv4 == UpdateStatus.invalidinput || resulterv6 == UpdateStatus.invalidinput)
                {
                    return new UnprocessableEntityObjectResult(UpdateStatus.invalidinput.ToString());
                } else {
                    return new ConflictObjectResult(resulterv4.ToString());
                }
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Exception thrown");
                return new InternalServerErrorResult();
            }
        }

        private void LogTokensToDebug(string token)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            foreach (var t in tokenList)
            {
                sb.Append(t);
                sb.Append(',');
            }
            sb.Append(']');
            var tokenListRaw = sb.ToString();
            this.logger.LogDebug($"Valid tokens: {tokenListRaw}\ttoken: {token}");
        }

        private List<string> GetAuthorizedUsers(IConfiguration authSection)
        {
            var children = authSection.GetChildren();
            var tokens = new List<string>();
            var clientUsername = authSection["clientUsername"];
            var clientPassword = authSection["clientPassword"];
            var userPassword = $"{clientUsername}:{clientPassword}";
            tokens.Add(Base64Encode(userPassword));
            return tokens;
        }

        private string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}