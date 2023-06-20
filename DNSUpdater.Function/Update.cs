﻿using System;
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
        private readonly string[] validKeys = new string[] { "hostname", "txt", "system" };
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
            string txtRecord = req.Query["txt"];
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
                (string.IsNullOrWhiteSpace(txtRecord) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(agent))
            {
                if (string.IsNullOrWhiteSpace(hostname))
                {
                    logger.LogWarning("Query parameter \"hostname\" is empty.");
                }

                if (string.IsNullOrWhiteSpace(txtRecord))
                {
                    logger.LogWarning("Query parameter \"txt\" are empty.");
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
                $"request details:	hostname: {hostname}	txt: {txtRecord}	token: {token}	agent: {agent}	system: {system}");

            token = token.Replace("Basic ", "");
            if (!tokenList.Contains(token))
            {
                this.logger.LogWarning($"Unauthorized request. Provided token");
                LogTokensToDebug(token);
                return new ObjectResult(UpdateStatus.badauth.ToString()) { StatusCode = 401 };
            }

            try
            {
                UpdateStatus resulterTxt = UpdateStatus.nochg;
                if (!string.IsNullOrWhiteSpace(txtRecord))
                {
                    logger.LogInformation($"Updating txt: {hostname}\ttxt: {txtRecord}");
                    var result = await this.service.UpdateTXTRecord(hostname, txtRecord, ttl);
                    resulterTxt = result;
                }

                if (resulterTxt  == UpdateStatus.good)
                {
                    return new OkObjectResult(UpdateStatus.good.ToString());
                } else {
                    return new ConflictObjectResult(resulterTxt.ToString());
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