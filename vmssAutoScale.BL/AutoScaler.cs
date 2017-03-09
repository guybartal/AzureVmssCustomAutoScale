using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using vmssAutoScale.Interfaces;

namespace vmssAutoScale.BL
{
    public delegate void TraceEventHandler(object sender, string message);


    public class AutoScaler
    {
        public event TraceEventHandler TraceEvent;

        private ILoadWatcher _loadWatcher;
        private int _maxThreshold, _minThreshold, _maxScale, _minScale;
        private string _clientId, _clientSecret, _tenantId, _azureArmApiBaseUrl, _subscriptionId, _resourceGroup, _vmssName, _vmssApiVersion;

        public AutoScaler(ILoadWatcher loadWatcher)
        {
            _loadWatcher = loadWatcher;

            if (!int.TryParse(ConfigurationManager.AppSettings["MaxThreshold"], out _maxThreshold))
            {
                _maxThreshold = 4;
                OnTraceEvent($"MaxThreshold Environment Variable is missing, setting MaxThreshold to {_maxThreshold}");
            }

            if (!int.TryParse(ConfigurationManager.AppSettings["MinThreshold"], out _minThreshold))
            {
                _minThreshold = 2;
                OnTraceEvent($"MinThreshold Environment Variable is missing, setting MinThreshold to {_minThreshold}");
            }

            if (!int.TryParse(ConfigurationManager.AppSettings["MaxScale"], out _maxScale))
            {
                _maxScale = 5;
                OnTraceEvent($"MaxThreshold Environment Variable is missing, setting MaxThreshold to {_maxScale}");
            }

            if (!int.TryParse(ConfigurationManager.AppSettings["MinScale"], out _minScale))
            {
                _minScale = 2;
                OnTraceEvent($"MinThreshold Environment Variable is missing, setting MinThreshold to {_minScale}");
            }

            _clientId = ConfigurationManager.AppSettings["ClientId"];
            _clientSecret = ConfigurationManager.AppSettings["ClientSecret"];
            _tenantId = ConfigurationManager.AppSettings["TenantId"];
            _azureArmApiBaseUrl = ConfigurationManager.AppSettings["AzureArmApiBaseUrl"];
            _subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];
            _resourceGroup = ConfigurationManager.AppSettings["ResourceGroup"];
            _vmssName = ConfigurationManager.AppSettings["VmssName"];
            _vmssApiVersion = ConfigurationManager.AppSettings["VmssApiVersion"];
        }

        public async Task AutoScale()
        {
            try
            {
                // sample load watcher for current queule lenght
                OnTraceEvent("querying load watcher");
                double length = _loadWatcher.GetCurrentLoad();
                OnTraceEvent($"Current load is {length}");

                // check if current queque lenght is above or below threshold
                if (length > _maxThreshold || length < _minThreshold)
                {
                    OnTraceEvent($"getting currect vmss sku...");
                    dynamic sku = await GetVMSSCapacityAsync();
                    //OnTraceEvent($"sku={JsonConvert.SerializeObject(sku)}");
                    int current = sku.capacity;

                    if (length > _maxThreshold)
                    {
                        if (current < _maxScale)
                        {
                            OnTraceEvent("Scaling vmss up...");
                            await ScaleAsync(sku, ScaleDirection.Up);
                        }
                        else
                        {
                            OnTraceEvent("Can't scale vmss up, scale set already reached upper limit...");
                        }
                    }
                    else
                    {
                        if (current > _minScale)
                        {
                            OnTraceEvent("Scaling vmss down...");
                            await ScaleAsync(sku, ScaleDirection.Down);
                        }
                        else
                        {
                            OnTraceEvent("Can't scale vmss down, scale set already reached lower limit...");
                        }
                    }
                }
                else
                {
                    OnTraceEvent("No need to scale...");
                }
            }
            catch(Exception ex)
            {
                OnTraceEvent(ex.ToString());
            }
        }

        private void OnTraceEvent(string message)
        {
            if (this.TraceEvent != null)
            {
                this.TraceEvent(this, message);
            }
        }

        private async Task<AuthenticationResult> GetAuthorizationHeaderAsync(string tenantId, string clientId, string clientSecret)
        {
            var context = new AuthenticationContext("https://login.windows.net/" + tenantId);

            var creds = new ClientCredential(clientId, clientSecret);

            return await context.AcquireTokenAsync("https://management.core.windows.net/", creds);
        }

        private async Task<string> SetVMSSCapacityAsync(dynamic sku)
        {
            try
            {
                AuthenticationResult authenticationResult = await GetAuthorizationHeaderAsync(_tenantId, _clientId, _clientSecret);
                var token = authenticationResult.CreateAuthorizationHeader();

                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_azureArmApiBaseUrl);
                    client.DefaultRequestHeaders.Add("Authorization", token);
                    client.DefaultRequestHeaders
                          .Accept
                          .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var url = $"subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{_vmssName}?api-version={_vmssApiVersion}";
                    var payload = $"{{sku:{JsonConvert.SerializeObject(sku)}}}";
                    HttpRequestMessage message = new HttpRequestMessage(new HttpMethod("PATCH"), url);

                    message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    using (HttpResponseMessage response = await client.SendAsync(message))
                    using (HttpContent content = response.Content)
                    {
                        if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            OnTraceEvent(response.StatusCode.ToString());
                        }
                        return await content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                OnTraceEvent(ex.ToString());
                return await Task.FromResult<string>(ex.ToString());
            }
        }

        private async Task<dynamic> GetVMSSCapacityAsync()
        {
            try
            {
                AuthenticationResult authenticationResult = await GetAuthorizationHeaderAsync(_tenantId, _clientId, _clientSecret);
                var token = authenticationResult.CreateAuthorizationHeader();

                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_azureArmApiBaseUrl);
                    client.DefaultRequestHeaders.Add("Authorization", token);
                    client.DefaultRequestHeaders
                          .Accept
                          .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var url = $"subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{_vmssName}?api-version={_vmssApiVersion}";

                    using (HttpResponseMessage response = await client.GetAsync(url))
                    using (HttpContent content = response.Content)
                    {
                        string json = await content.ReadAsStringAsync();
                        dynamic dyn = JObject.Parse(json);
                        return dyn.sku;
                    }
                }
            }
            catch (Exception ex)
            {
                OnTraceEvent(ex.ToString());
                return await Task.FromResult<string>(ex.ToString());
            }
        }


        private async Task<string> ScaleAsync(dynamic Sku, ScaleDirection scaleDirection)
        {
            int current = Sku.capacity;

            if (scaleDirection == ScaleDirection.Up)
            {
                Sku.capacity += 1;
            }
            else
            {
                Sku.capacity -= 1;
            }

            OnTraceEvent($"setting currect vmss capacity from {current} to {JsonConvert.SerializeObject(Sku.capacity)}");
            return await SetVMSSCapacityAsync(Sku);
        }
    }
}
