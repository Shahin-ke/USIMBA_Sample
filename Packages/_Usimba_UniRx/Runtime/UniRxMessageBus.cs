using System;
using System.Collections.Generic;
using System.Linq;
using SH_SWAT.Usimba.EventOrientedModule.CQRS;
using SH_SWAT.Usimba.Validation;
using UniRx;
using UnityEngine;

namespace SH_SWAT.Usimba.MessageBus.UniRxExtension
{
    public class UniRxMessageBus : IMessageBus
    {
        private readonly bool _assertWarnings;

        // ReSharper disable once InconsistentNaming
        public readonly Subject<IMessage> _messages = new Subject<IMessage>();
        public IObservable<IMessage> Messages => _messages;

        public List<MessageRouteRule> Rules = new List<MessageRouteRule>();
        public SubscriptionRegistery RuleSubscriptions = new SubscriptionRegistery();

        private readonly List<RegisteryItem> _subscribedHandlerWithoutRule = new List<RegisteryItem>();

        public UniRxMessageBus(bool assertWarnings)
        {
            _assertWarnings = assertWarnings;
        }

        public void RaiseEvent(IEvent evt)
        {
            if (null == evt)
            {
                return;
            }

            _messages.OnNext(evt);
        }

        public void SendCommand(ICommand cmd)
        {
            if (null == cmd)
            {
                return;
            }

            var validators = GetValidators(cmd);
            var validationContext = new ValidationContext();

            var lastValidatorResult = Usimba.Validation.ValidationResult.Accepted;
            for(var i = 0 ; i < validators.Count && lastValidatorResult == ValidationResult.Accepted; ++i)
            {
                var validator = validators[i];
                lastValidatorResult = validator.IsValid(cmd, validationContext);
            }

            if (ValidationResult.Accepted == lastValidatorResult)
            {
                _messages.OnNext(cmd);
            }
        }

        private List<IValidator> GetValidators(ICommand cmd)
        {
            //var cmdType = cmd.GetType();

            // TODO: Should find validators by validation or settings

            return new List<IValidator>();
        }

        public void Subscribe<TMessageHandler, TMessage>(TMessageHandler handler, IMessageHandlerActionExecutor methodSelector)
            where TMessage : class, IMessage
            where TMessageHandler : class, IMessageHandler<TMessage>
        {

            Subscribe(handler, typeof(TMessage), methodSelector);
        }

        private void Subscribe(IMessageHandler handler, Type messageType, IMessageHandlerActionExecutor methodSelector)
        {
            var messageHandlerType = handler.GetType();
            var rules = GetRule(messageHandlerType, messageType);

            var conditionResult = null != rules && rules.Count > 0;
            if (_assertWarnings)
            {
                UnityEngine.Assertions.Assert.IsTrue(conditionResult,
                    $"Message Rule for {messageHandlerType.Name}<{messageType}> Not Found.");
            }

            if (conditionResult)
            {
                foreach (var rule in rules)
                {
                    var items = RuleSubscriptions.ItemsByRule(rule);
                    var item = items.FirstOrDefault(it => it.Handler == handler);
                    if (null == item)
                    {
                        IObservable<IMessage> observable;

                        if (null != rule.Transformer)
                        {
                            observable =
                                Messages.Where(
                                    message =>
                                        rule.IncludeDerivedMessageTypes
                                            ? rule.Transformer.InputType.IsInstanceOfType(message)
                                            : message.GetType() == rule.Transformer.InputType);

                            if (null != rule.PreCondition)
                            {
                                observable = observable.Where(message => EventOrientedModule.CQRS.Validators.ValidationResult.Accepted == rule.PreCondition.Validate(handler, message));
                            }
                            observable = observable.Select(message => Convert.ChangeType(rule.Transformer.Transform(message), rule.Transformer.OutputType) as IMessage);
                        }
                        else
                        {
                            observable = Messages.Where(message => rule.IncludeDerivedMessageTypes ? messageType.IsInstanceOfType(message) : messageType == message.GetType());
                        }

                        if (null != rule.PostCondition)
                        {
                            observable = observable.Where(message => EventOrientedModule.CQRS.Validators.ValidationResult.Accepted == rule.PostCondition.Validate(handler, message));
                        }

                        var subscription = observable.Subscribe(message => methodSelector.Execute(message));

                        RuleSubscriptions.Add(new SubscriptionRegisteryItem()
                        {
                            Route = rule,
                            Handler = handler,
                            MethodInfo = methodSelector,
                            MessagehandlerType = messageHandlerType,
                            MessageType = messageType,
                            Subscription = subscription
                        });
                    }
                    else
                    {
                        Debug.LogWarning("Duplicate Subscription.");
                    }
                }
            }
            else
            {
                var registeryItem = new RegisteryItem(messageType, handler, methodSelector);
                _subscribedHandlerWithoutRule.Add(registeryItem);
            }
        }

        public void Unsubscribe<TMessageHandler, TMessage>(TMessageHandler handler)
            where TMessage : class, IMessage
            where TMessageHandler : class, IMessageHandler<TMessage>
        {
            var rule = GetRule<TMessageHandler, TMessage>();

            UnityEngine.Assertions.Assert.IsNotNull(rule, $"Message Rule for {typeof(TMessageHandler)}<{typeof(TMessage)}> Not Found.");
            if (null == rule) return;

            var subscriptions = RuleSubscriptions.GetRuleSubscriptions(rule)?.ToList();
            if (null == subscriptions || subscriptions.Count <= 0) return;

            var handlerSubscriptions = subscriptions.Where(item => item.Handler == handler).ToList();
            var handlerSubscriptionsCount = handlerSubscriptions.Count;
            if (handlerSubscriptionsCount <= 0) return;

            for (var i = handlerSubscriptionsCount - 1; i >= 0; --i)
            {
                var subscription = subscriptions[i];
                subscription.Subscription.Dispose();
                subscriptions.Remove(subscription);
            }
        }

        public void AddRule(MessageRouteRule rule)
        {
            Rules.Add(rule);

            //var items =
            //    _subscribedHandlerWithoutRule.Where(item => item.HandlerType == rule.HandlerType && item.MessageType == rule.MessageType)
            //        .ToList();

            var items =
                _subscribedHandlerWithoutRule.Where(
                        item => IsTypeAcceptedByRule(rule, item.HandlerType, item.MessageType))
                    .ToList();

            if (items.Count <= 0)
            {
                return;
            }

            for (var i = items.Count - 1; i >= 0; --i)
            {
                var item = items[i];

                dynamic objHandler;
                var targetAvaliable = item.Handler.TryGetTarget(out objHandler);
                if (!targetAvaliable) continue;

                Subscribe(objHandler, item.MessageType, item.HandlerMethodSelector);

                items.Remove(item);
            }
        }

        private MessageRouteRule GetRule<TMessageHandler, TMessage>() where TMessage : IMessage where TMessageHandler : IMessageHandler<TMessage>
        {
            return Rules.FirstOrDefault(
                roteRule => typeof(TMessageHandler) == roteRule.HandlerType && typeof(TMessage) == roteRule.MessageType);
        }

        private List<MessageRouteRule> GetRule(Type messageHandlerType, Type messageType)
        {
            return Rules.Where(
                roteRule => IsTypeAcceptedByRule(roteRule, messageHandlerType, messageType))
                .ToList();
        }

        private bool IsTypeAcceptedByRule(MessageRouteRule rule, Type messageHandlerType, Type messageType)
        {
            return (rule.IncludeDerivedHandlerTypes
                       ? rule.HandlerType.IsAssignableFrom(messageHandlerType)
                       : messageHandlerType == rule.HandlerType)
                   &&
                   (rule.IncludeDerivedMessageTypes
                       ? rule.MessageType.IsAssignableFrom(messageType)
                       : messageType == rule.MessageType);
        }
    }
}

