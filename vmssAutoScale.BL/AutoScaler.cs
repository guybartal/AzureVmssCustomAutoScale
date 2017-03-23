using Microsoft.ApplicationInsights;
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
        private int _maxThreshold, _minThreshold, _maxScale, _minScale, _scaleOutBy, _scaleInBy;
        private string _clientId, _clientSecret, _tenantId, _azureArmApiBaseUrl, _subscriptionId, _resourceGroup, _vmssName, _vmssApiVersion,
            _applictionInsightsInstrumentationKey,
            _applicationInsightsLoadWacherMetricName;

        private const string AI_VMSS_CAPACITY_METRIC_NAME = "VM Scale Set Servers Capacity";
        private const string AI_SCALE_OUT_EVENT_NAME = "VM Scale Set Scale Out";
        private const string AI_SCALE_IN_EVENT_NAME = "VM Scale Set Scale Out";


        private TelemetryClient _telemetry;

        private void LoadIntegerParameter(string parameterName, int defaultValue, out int parameter)
        {
            if (!int.TryParse(ConfigurationManager.AppSettings[parameterName], out parameter))
            {
                parameter = defaultValue;
                OnTraceEvent($"{parameterName} Environment Variable is missing, setting {parameterName} to {defaultValue}");
            }
        }

        public AutoScaler(ILoadWatcher loadWatcher)
        {
            _loadWatcher = loadWatcher;
            
            LoadIntegerParameter("MaxThreshold", 4, out _maxThreshold);
            LoadIntegerParameter("MinThreshold", 2, out _minThreshold);
            LoadIntegerParameter("MaxScale", 5, out _maxScale);
            LoadIntegerParameter("MinScale", 2, out _minScale);
            LoadIntegerParameter("ScaleOutBy", 1, out _scaleOutBy);
            LoadIntegerParameter("ScaleInBy", 1, out _scaleInBy);

            _clientId = ConfigurationManager.AppSettings["ClientId"];
            _clientSecret = ConfigurationManager.AppSettings["ClientSecret"];
            _tenantId = ConfigurationManager.AppSettings["TenantId"];
            _azureArmApiBaseUrl = ConfigurationManager.AppSettings["AzureArmApiBaseUrl"];
            _subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];
            _resourceGroup = ConfigurationManager.AppSettings["ResourceGroup"];
            _vmssName = ConfigurationManager.AppSettings["VmssName"];
            _vmssApiVersion = ConfigurationManager.AppSettings["VmssApiVersion"];
            _applictionInsightsInstrumentationKey = ConfigurationManager.AppSettings["ApplictionInsightsInstrumentationKey"];


            if (!string.IsNullOrEmpty(_applictionInsightsInstrumentationKey))
            {
                _applicationInsightsLoadWacherMetricName = ConfigurationManager.AppSettings["ApplicationInsightsLoadWacherMetricName"];
                _telemetry = new TelemetryClient();
                _telemetry.InstrumentationKey = _applictionInsightsInstrumentationKey;
            }
        }

        public async Task AutoScale()
        {
            try
            {
                // sample load watcher for current queule lenght
                OnTraceEvent("Querying load watcher");
                double currentLoad = _loadWatcher.GetCurrentLoad();
                _telemetry?.TrackMetric(_applicationInsightsLoadWacherMetricName, currentLoad);
                OnTraceEvent($"Current load is {currentLoad:N2}");

                OnTraceEvent($"Getting currect vmss sku...");
                dynamic sku = await GetVMSSCapacityAsync();
                //OnTraceEvent($"sku={JsonConvert.SerializeObject(sku)}");
                int current = sku.capacity;
                _telemetry?.TrackMetric(AI_VMSS_CAPACITY_METRIC_NAME, current);
                OnTraceEvent($"Scale set capacity is {current}");

                // check if current queque lenght is above or below threshold
                if (currentLoad > _maxThreshold || currentLoad < _minThreshold)
                {
                    if (currentLoad > _maxThreshold)
                    {
                        if (current < _maxScale)
                        {
                            OnTraceEvent($"Current load reached upper threshold of {_maxThreshold}, scaling vmss out by {_scaleOutBy} servers...");
                            _telemetry.TrackEvent(AI_SCALE_OUT_EVENT_NAME, null , new Dictionary<string, double> { [AI_VMSS_CAPACITY_METRIC_NAME] = current, [_applicationInsightsLoadWacherMetricName] = currentLoad } );
                            await ScaleAsync(sku, ScaleDirection.Out);
                        }
                        else
                        {
                            OnTraceEvent($"Can't scale vmss out, scale set already reached upper limit of {_maxScale}...");
                        }
                    }
                    else
                    {
                        if (current > _minScale)
                        {
                            OnTraceEvent($"Current load reached lower threshold of {_minThreshold}, scaling vmss in by {_scaleInBy} servers...");
                            _telemetry.TrackEvent(AI_SCALE_IN_EVENT_NAME, null, new Dictionary<string, double> { [AI_VMSS_CAPACITY_METRIC_NAME] = current, [_applicationInsightsLoadWacherMetricName] = currentLoad });
                            await ScaleAsync(sku, ScaleDirection.In);
                        }
                        else
                        {
                            OnTraceEvent($"Can't scale vmss down, scale set already reached lower limit of {_minScale}...");
                        }
                    }
                }
                else
                {
                    OnTraceEvent($"No need to scale, current load is between lower threshold {_minThreshold} and upper {_maxThreshold}...");
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

            if (scaleDirection == ScaleDirection.Out)
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
