# Azure VMSS Custom Auto Scale
Currently Azure Virutual Machine Scale Sets (VMSS) doesn't support custom logic based [auto scaling](https://docs.microsoft.com/en-us/azure/monitoring-and-diagnostics/insights-advanced-autoscale-virtual-machine-scale-sets).

This solution was developed for real customer need,
It based on Azure Function App which triggered by timer and samples SQL stored procedure to get current load on VMSS,
Azure Function then adds or removes VMs accordingly to configured threshold.

You can clone this repo and create your own custom logic, just create a new class library and implement [ILoadWatcher](https://github.com/guybartal/AzureVmssCustomAutoScale/blob/master/vmssAutoScale.Interfaces/ILoadWatcher.cs) interface.

There's also console app for testing locally.

## Requirements
* Azure Function App
* VMSS
* [Register](https://docs.microsoft.com/en-us/azure/active-directory/active-directory-app-registration) a new application in Azure Active Directory (need to be global admin in order to do that)
* Create application Key, save this secret, you will need that later for deployment
* Give the application "Owner" role on VMSS
* Install [Visual Studio Tools for Azure Functions](https://blogs.msdn.microsoft.com/webdev/2016/12/01/visual-studio-tools-for-azure-functions/)

## Deployment
* Open solution in visual studio 2015 update 3
* Build solution
* Publish Azure Function project named "vmssAutoScale" into your Azure Function App
* Set Azure Function App settings:
```XML
  <appSettings>
    <add key="MaxScale" value="maximum server capacity limit"/>
    <add key="MinScale" value="minimum server capacity limit"/>
    <add key="MaxThreshold" value="maximum threshold for auto scale, above this value autoscaler will add one server to vmss"/>
    <add key="MinThreshold" value="minimum threshold for auto scale, below this value autoscaler will remove one server to vmss"/>
    <add key="SQLConnectionString" value="sql server connection string which holds logic for autoscale"/>
    <add key="ClientId" value="application key in azure active directory"/>
    <add key="ClientSecret" value="application secret in azure active directory"/>
    <add key="TenantId" value="active directory id"/>
    <add key="SubscriptionId" value="azure subscription id which holds vmss"/>
    <add key="ResourceGroup" value="vmss resource group"/>
    <add key="VmssName" value="vmss name"/>
    <add key="AzureArmApiBaseUrl" value="https://management.azure.com/"/>
    <add key="VmssApiVersion" value="2016-03-30"/>
  </appSettings>
 ```
