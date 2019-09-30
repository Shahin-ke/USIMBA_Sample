# USIMBA
Simple MessageBus for unity game engine

## Using on Zenject Installer
1- Create Script file on project
2- Add to Zenject Scene or Project Context
```C#
using SH_SWAT.Usimba.MessageBus.UniRxExtension;
using System;
using SH_SWAT.Usimba.EventOrientedModule.CQRS;
using Zenject;

namespace SH_SWAT.Usimba.MessageBus
{
    public class MessageBusInstaller : MonoInstaller<MessageBusInstaller>
    {
        public MessageBusInstasllerSettings settings;

        public override void InstallBindings()
        {
            BaseMessageBusInstaller.Install(Container, settings);
        }
    }

    public class BaseMessageBusInstaller : Installer<MessageBusInstasllerSettings, BaseMessageBusInstaller>
    {
        private readonly MessageBusInstasllerSettings _settings;

        public BaseMessageBusInstaller(MessageBusInstasllerSettings settings)
        {
            _settings = settings;
        }

        public override void InstallBindings()
        {
            Container.Bind<IMessageBus>()
                .To<UniRxMessageBus>()
                .AsSingle()
                .WithArguments(_settings.AssertBusInfo);
        }
    }

    [Serializable]
    public class MessageBusInstasllerSettings
    {
        public bool AssertBusInfo;
    }
}
```