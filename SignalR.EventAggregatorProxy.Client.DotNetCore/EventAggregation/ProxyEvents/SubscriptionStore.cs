﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SignalR.EventAggregatorProxy.Client.DotNetCore.Constraint;
using SignalR.EventAggregatorProxy.Client.DotNetCore.Extensions;
using SignalR.EventAggregatorProxy.Client.DotNetCore.Model;

namespace SignalR.EventAggregatorProxy.Client.DotNetCore.EventAggregation.ProxyEvents
{
    public class SubscriptionStore : ISubscriptionStore
    {
        private readonly Dictionary<Type, List<Subscription>> eventSubscriptions;
        private readonly Dictionary<Type, List<IConstraintInfo>> eventConstraints;
        private readonly Dictionary<object, IEnumerable<IConstraintInfo>> subscriberConstraints;

        public SubscriptionStore()
        {
            eventSubscriptions = new Dictionary<Type, List<Subscription>>();
            eventConstraints = new Dictionary<Type, List<IConstraintInfo>>();
            subscriberConstraints = new Dictionary<object, IEnumerable<IConstraintInfo>>();
        }

        public IEnumerable<Subscription> GetActualSubscriptions(IEnumerable<Subscription> newSubscriptions)
        {
            var uniqueSubscription = newSubscriptions
                .Where(UniqueSubscription)
                .ToList();

            newSubscriptions.ForEach(AddSubscription);

            return uniqueSubscription;
        }

        public IEnumerable<Subscription> PopSubscriptions(IEnumerable<Type> eventTypes, object subscriber)
        {
            var actualUnsubscriptions = new List<Subscription>();

            foreach (var eventType in eventTypes)
            {
                var subscriptions = eventSubscriptions[eventType];

                var remove = subscriptions
                    .ToList()
                    .Where(s => !s.ConstraintId.HasValue || subscriberConstraints[subscriber].Any(c => c.Id == s.ConstraintId))
                    .GroupBy(s => s.ConstraintId)
                    .ForEach(g => subscriptions.Remove(g.First()))
                    .Where(g => g.Count() == 1).Select(g => g.First())
                    .ToList();

                actualUnsubscriptions.AddRange(remove);
                RemoveConstraint(eventType, remove);
            }

            subscriberConstraints.Remove(subscriber);

            return actualUnsubscriptions;
        }

        public IEnumerable<Subscription> ListUniqueSubscriptions()
        {
            var sub = eventSubscriptions.SelectMany(s => s.Value)
                .GroupBy(s => s.EventType)
                .SelectMany(g => g.GroupBy(s => s.ConstraintId).Select(c => c.First()).ToList())
                .ToList();
            return sub;
        }

        public void AddConstraints(object subscriber, IEnumerable<IConstraintInfo> constraints)
        {
            subscriberConstraints[subscriber] = constraints;
        }

        public bool HasConstraint(object subscriber, int constraintId)
        {
            return subscriberConstraints[subscriber].Any(c => c.Id == constraintId);
        }

        public void AddConstraint<TEvent, TConstraint>(ConstraintInfo<TEvent, TConstraint> constraintInfo)
        {
            constraintInfo.Id = GenerateConstraintId<TEvent>(constraintInfo);
        }

        private int GenerateConstraintId<TEvent>(IConstraintInfo constraint)
        {
            var eventType = typeof (TEvent);

            var hash = (eventType.FullName + JsonConvert.SerializeObject(constraint.GetConstraint())).GetHashCode();

            if (!eventConstraints.ContainsKey(eventType))
            {
                AddConstraint<TEvent>(constraint);
                return hash;
            }

            var existing = eventConstraints[eventType].FirstOrDefault(c => c.Id == hash);
            if(existing == null)
                AddConstraint<TEvent>(constraint);

            return hash;
        }

        private void RemoveConstraint(Type eventType, IEnumerable<Subscription> actualUnsubscriptions)
        {
            if(!eventConstraints.ContainsKey(eventType)) return;

            eventConstraints[eventType].RemoveAll(ec => actualUnsubscriptions.Any(s => ec.Id == s.ConstraintId));
            if (!eventConstraints[eventType].Any())
                eventConstraints.Remove(eventType);
        }


        private void AddConstraint<TEvent>(IConstraintInfo constraint)
        {
            var eventType = typeof (TEvent);
            if(!eventConstraints.ContainsKey(eventType))
                eventConstraints[eventType] = new List<IConstraintInfo>();

            eventConstraints[eventType].Add(constraint);
        }

        private void AddSubscription(Subscription subscription)
        {
            if(!eventSubscriptions.ContainsKey(subscription.EventType)) 
                eventSubscriptions[subscription.EventType] = new List<Subscription>();

            eventSubscriptions[subscription.EventType].Add(subscription);
        }

        private bool UniqueSubscription(Subscription subscription)
        {
            if (!eventSubscriptions.ContainsKey(subscription.EventType)) return true;

            if (subscription.Constraint == null) return false;

            return ConstraintUnique(subscription);
        }

        private bool ConstraintUnique(Subscription subscription)
        {
            return eventSubscriptions[subscription.EventType]
                .Where(s => s != subscription)
                .All(s => s.ConstraintId != subscription.ConstraintId);
        }
        
    }
}
