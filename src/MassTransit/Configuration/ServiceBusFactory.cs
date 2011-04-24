// Copyright 2007-2011 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit
{
	using System;
	using BusConfigurators;
	using Configuration;
	using Configurators;
	using Magnum;
	using Util;

	/// <summary>
	/// The starting point to configure and create a service bus instance
	/// </summary>
	public static class ServiceBusFactory
	{
		static readonly ServiceBusConfiguratorDefaultSettings _defaultSettings = new ServiceBusConfiguratorDefaultSettings();

		[NotNull]
		public static IServiceBus New([NotNull] Action<ServiceBusConfigurator> configure)
		{
			Guard.AgainstNull(configure, "configure");

			var configurator = new ServiceBusConfiguratorImpl(_defaultSettings);

			configure(configurator);

			configurator.Validate();

			return configurator.CreateServiceBus();
		}

		public static void ConfigureDefaultSettings([NotNull] Action<ServiceBusDefaultSettingsConfigurator> configure)
		{
			Guard.AgainstNull(configure);

			var configurator = new ServiceBusDefaultSettingsConfiguratorImpl(_defaultSettings);

			configure(configurator);
		}
	}
}