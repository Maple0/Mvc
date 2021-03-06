// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.Infrastructure
{
    // Most of the in-depth testing for SelectCandidates is part of the descision tree tests.
    // This is just basic coverage of the API in common scenarios.
    public class DefaultActionSelectorTests
    {
        [Fact]
        public void SelectCandidates_SingleMatch()
        {
            var actions = new ActionDescriptor[]
            {
                new ActionDescriptor()
                {
                    DisplayName = "A1",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    },
                },
                new ActionDescriptor()
                {
                    DisplayName = "A2",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "About" }
                    },
                },
            };

            var selector = CreateSelector(actions);

            var routeContext = CreateRouteContext("GET");
            routeContext.RouteData.Values.Add("controller", "Home");
            routeContext.RouteData.Values.Add("action", "Index");

            // Act
            var candidates = selector.SelectCandidates(routeContext);

            // Assert
            Assert.Collection(candidates, (a) => Assert.Same(actions[0], a));
        }

        [Fact]
        public void SelectCandidates_MultipleMatches()
        {
            var actions = new ActionDescriptor[]
            {
                new ActionDescriptor()
                {
                    DisplayName = "A1",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    },
                },
                new ActionDescriptor()
                {
                    DisplayName = "A2",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    },
                },
            };

            var selector = CreateSelector(actions);

            var routeContext = CreateRouteContext("GET");
            routeContext.RouteData.Values.Add("controller", "Home");
            routeContext.RouteData.Values.Add("action", "Index");

            // Act
            var candidates = selector.SelectCandidates(routeContext);

            // Assert
            Assert.Equal(actions.ToArray(), candidates.ToArray());
        }

        [Fact]
        public void SelectCandidates_NoMatch()
        {
            var actions = new ActionDescriptor[]
            {
                new ActionDescriptor()
                {
                    DisplayName = "A1",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    },
                },
                new ActionDescriptor()
                {
                    DisplayName = "A2",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "About" }
                    },
                },
            };

            var selector = CreateSelector(actions);

            var routeContext = CreateRouteContext("GET");
            routeContext.RouteData.Values.Add("controller", "Foo");
            routeContext.RouteData.Values.Add("action", "Index");

            // Act
            var candidates = selector.SelectCandidates(routeContext);

            // Assert
            Assert.Empty(candidates);
        }

        [Fact]
        public void SelectCandidates_NoMatch_ExcludesAttributeRoutedActions()
        {
            var actions = new ActionDescriptor[]
            {
                new ActionDescriptor()
                {
                    DisplayName = "A1",
                    RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    },
                    AttributeRouteInfo = new AttributeRouteInfo()
                    {
                        Template = "/Home",
                    }
                },
            };

            var selector = CreateSelector(actions);

            var routeContext = CreateRouteContext("GET");
            routeContext.RouteData.Values.Add("controller", "Home");
            routeContext.RouteData.Values.Add("action", "Index");

            // Act
            var candidates = selector.SelectCandidates(routeContext);

            // Assert
            Assert.Empty(candidates);
        }

        [Fact]
        public void SelectBestCandidate_AmbiguousActions_LogIsCorrect()
        {
            // Arrange
            var sink = new TestSink();
            var loggerFactory = new TestLoggerFactory(sink, enabled: true);

            var actions = new ActionDescriptor[]
            {
                new ActionDescriptor() { DisplayName = "A1" },
                new ActionDescriptor() { DisplayName = "A2" },
            };
            var selector = CreateSelector(actions, loggerFactory);

            var routeContext = CreateRouteContext("POST");
            var actionNames = string.Join(Environment.NewLine, actions.Select(action => action.DisplayName));
            var expectedMessage = "Request matched multiple actions resulting in " +
                $"ambiguity. Matching actions: {actionNames}";

            // Act
            Assert.Throws<AmbiguousActionException>(() => { selector.SelectBestCandidate(routeContext, actions); });

            // Assert
            Assert.Empty(sink.Scopes);
            Assert.Single(sink.Writes);
            Assert.Equal(expectedMessage, sink.Writes[0].State?.ToString());
        }

        [Fact]
        public void SelectBestCandidate_PrefersActionWithConstraints()
        {
            // Arrange
            var actionWithConstraints = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new HttpMethodActionConstraint(new string[] { "POST" }),
                },
                Parameters = new List<ParameterDescriptor>(),
            };

            var actionWithoutConstraints = new ActionDescriptor()
            {
                Parameters = new List<ParameterDescriptor>(),
            };

            var actions = new ActionDescriptor[] { actionWithConstraints, actionWithoutConstraints };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, actionWithConstraints);
        }

        [Fact]
        public void SelectBestCandidate_ConstraintsRejectAll()
        {
            // Arrange
            var action1 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = false, },
                },
            };

            var action2 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = false, },
                },
            };

            var actions = new ActionDescriptor[] { action1, action2 };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Null(action);
        }

        [Fact]
        public void SelectBestCandidate_ConstraintsRejectAll_DifferentStages()
        {
            // Arrange
            var action1 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = false, Order = 0 },
                    new BooleanConstraint() { Pass = true, Order = 1 },
                },
            };

            var action2 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0 },
                    new BooleanConstraint() { Pass = false, Order = 1 },
                },
            };

            var actions = new ActionDescriptor[] { action1, action2 };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Null(action);
        }

        [Fact]
        public void SelectBestCandidate_ActionConstraintFactory()
        {
            // Arrange
            var actionWithConstraints = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new ConstraintFactory()
                    {
                        Constraint = new BooleanConstraint() { Pass = true },
                    },
                }
            };

            var actionWithoutConstraints = new ActionDescriptor()
            {
                Parameters = new List<ParameterDescriptor>(),
            };

            var actions = new ActionDescriptor[] { actionWithConstraints, actionWithoutConstraints };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, actionWithConstraints);
        }

        [Fact]
        public void SelectBestCandidate_ActionConstraintFactory_ReturnsNull()
        {
            // Arrange
            var nullConstraint = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new ConstraintFactory()
                    {
                    },
                }
            };

            var actions = new ActionDescriptor[] { nullConstraint };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, nullConstraint);
        }

        // There's a custom constraint provider registered that only understands BooleanConstraintMarker
        [Fact]
        public void SelectBestCandidate_CustomProvider()
        {
            // Arrange
            var actionWithConstraints = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraintMarker() { Pass = true },
                }
            };

            var actionWithoutConstraints = new ActionDescriptor()
            {
                Parameters = new List<ParameterDescriptor>(),
            };

            var actions = new ActionDescriptor[] { actionWithConstraints, actionWithoutConstraints, };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, actionWithConstraints);
        }

        // Due to ordering of stages, the first action will be better.
        [Fact]
        public void SelectBestCandidate_ConstraintsInOrder()
        {
            // Arrange
            var best = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0, },
                },
            };

            var worst = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 1, },
                },
            };

            var actions = new ActionDescriptor[] { best, worst };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, best);
        }

        // Due to ordering of stages, the first action will be better.
        [Fact]
        public void SelectBestCandidate_ConstraintsInOrder_MultipleStages()
        {
            // Arrange
            var best = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0, },
                    new BooleanConstraint() { Pass = true, Order = 1, },
                    new BooleanConstraint() { Pass = true, Order = 2, },
                },
            };

            var worst = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0, },
                    new BooleanConstraint() { Pass = true, Order = 1, },
                    new BooleanConstraint() { Pass = true, Order = 3, },
                },
            };

            var actions = new ActionDescriptor[] { best, worst };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, best);
        }

        [Fact]
        public void SelectBestCandidate_Fallback_ToActionWithoutConstraints()
        {
            // Arrange
            var nomatch1 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0, },
                    new BooleanConstraint() { Pass = true, Order = 1, },
                    new BooleanConstraint() { Pass = false, Order = 2, },
                },
            };

            var nomatch2 = new ActionDescriptor()
            {
                ActionConstraints = new List<IActionConstraintMetadata>()
                {
                    new BooleanConstraint() { Pass = true, Order = 0, },
                    new BooleanConstraint() { Pass = true, Order = 1, },
                    new BooleanConstraint() { Pass = false, Order = 3, },
                },
            };

            var best = new ActionDescriptor();

            var actions = new ActionDescriptor[] { best, nomatch1, nomatch2 };

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("POST");

            // Act
            var action = selector.SelectBestCandidate(context, actions);

            // Assert
            Assert.Same(action, best);
        }

        [Fact]
        public void SelectBestCandidate_Ambiguous()
        {
            // Arrange
            var expectedMessage =
                "Multiple actions matched. " +
                "The following actions matched route data and had all constraints satisfied:" + Environment.NewLine +
                Environment.NewLine +
                "Ambiguous1" + Environment.NewLine +
                "Ambiguous2";

            var actions = new ActionDescriptor[]
            {
                CreateAction(area: null, controller: "Store", action: "Buy"),
                CreateAction(area: null, controller: "Store", action: "Buy"),
            };

            actions[0].DisplayName = "Ambiguous1";
            actions[1].DisplayName = "Ambiguous2";

            var selector = CreateSelector(actions);
            var context = CreateRouteContext("GET");

            context.RouteData.Values.Add("controller", "Store");
            context.RouteData.Values.Add("action", "Buy");

            // Act
            var ex = Assert.Throws<AmbiguousActionException>(() =>
            {
                selector.SelectBestCandidate(context, actions);
            });

            // Assert
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        [InlineData("POST")]
        [InlineData("DELETE")]
        [InlineData("PATCH")]
        public void HttpMethodAttribute_ActionWithMultipleHttpMethodAttributeViaAcceptVerbs_ORsMultipleHttpMethods(string verb)
        {
            // Arrange
            var routeContext = new RouteContext(GetHttpContext(verb));
            routeContext.RouteData.Values.Add("controller", "HttpMethodAttributeTests_RestOnly");
            routeContext.RouteData.Values.Add("action", "Patch");

            // Act
            var result = InvokeActionSelector(routeContext);

            // Assert
            Assert.Equal("Patch", result.ActionName);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        [InlineData("POST")]
        [InlineData("DELETE")]
        [InlineData("PATCH")]
        [InlineData("HEAD")]
        public void HttpMethodAttribute_ActionWithMultipleHttpMethodAttributes_ORsMultipleHttpMethods(string verb)
        {
            // Arrange
            var routeContext = new RouteContext(GetHttpContext(verb));
            routeContext.RouteData.Values.Add("controller", "HttpMethodAttributeTests_RestOnly");
            routeContext.RouteData.Values.Add("action", "Put");

            // Act
            var result = InvokeActionSelector(routeContext);

            // Assert
            Assert.Equal("Put", result.ActionName);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        public void HttpMethodAttribute_ActionDecoratedWithHttpMethodAttribute_OverridesConvention(string verb)
        {
            // Arrange
            // Note no action name is passed, hence should return a null action descriptor.
            var routeContext = new RouteContext(GetHttpContext(verb));
            routeContext.RouteData.Values.Add("controller", "HttpMethodAttributeTests_RestOnly");

            // Act
            var result = InvokeActionSelector(routeContext);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("Put")]
        [InlineData("RPCMethod")]
        [InlineData("RPCMethodWithHttpGet")]
        public void NonActionAttribute_ActionNotReachable(string actionName)
        {
            // Arrange
            var actionDescriptorProvider = GetActionDescriptorProvider();

            // Act
            var result = actionDescriptorProvider
                .GetDescriptors()
                .FirstOrDefault(x => x.ControllerName == "NonAction" && x.ActionName == actionName);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        [InlineData("POST")]
        [InlineData("DELETE")]
        [InlineData("PATCH")]
        public void ActionNameAttribute_ActionGetsExposedViaActionName_UnreachableByConvention(string verb)
        {
            // Arrange
            var routeContext = new RouteContext(GetHttpContext(verb));
            routeContext.RouteData.Values.Add("controller", "ActionName");
            routeContext.RouteData.Values.Add("action", "RPCMethodWithHttpGet");

            // Act
            var result = InvokeActionSelector(routeContext);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("GET", "CustomActionName_Verb")]
        [InlineData("PUT", "CustomActionName_Verb")]
        [InlineData("POST", "CustomActionName_Verb")]
        [InlineData("DELETE", "CustomActionName_Verb")]
        [InlineData("PATCH", "CustomActionName_Verb")]
        [InlineData("GET", "CustomActionName_DefaultMethod")]
        [InlineData("PUT", "CustomActionName_DefaultMethod")]
        [InlineData("POST", "CustomActionName_DefaultMethod")]
        [InlineData("DELETE", "CustomActionName_DefaultMethod")]
        [InlineData("PATCH", "CustomActionName_DefaultMethod")]
        [InlineData("GET", "CustomActionName_RpcMethod")]
        [InlineData("PUT", "CustomActionName_RpcMethod")]
        [InlineData("POST", "CustomActionName_RpcMethod")]
        [InlineData("DELETE", "CustomActionName_RpcMethod")]
        [InlineData("PATCH", "CustomActionName_RpcMethod")]
        public void ActionNameAttribute_DifferentActionName_UsesActionNameFromActionNameAttribute(string verb, string actionName)
        {
            // Arrange
            var routeContext = new RouteContext(GetHttpContext(verb));
            routeContext.RouteData.Values.Add("controller", "ActionName");
            routeContext.RouteData.Values.Add("action", actionName);

            // Act
            var result = InvokeActionSelector(routeContext);

            // Assert
            Assert.Equal(actionName, result.ActionName);
        }

        private ControllerActionDescriptor InvokeActionSelector(RouteContext context)
        {
            var actionDescriptorProvider = GetActionDescriptorProvider();

            var serviceContainer = new ServiceCollection();
            var list = new List<IActionDescriptorProvider>()
            {
                actionDescriptorProvider,
            };

            serviceContainer.AddSingleton(typeof(IEnumerable<IActionDescriptorProvider>), list);

            var actionDescriptorCollectionProvider = new ActionDescriptorCollectionProvider(
                serviceContainer.BuildServiceProvider());
            var decisionTreeProvider = new ActionSelectorDecisionTreeProvider(actionDescriptorCollectionProvider);

            var actionConstraintProviders = new[]
            {
                new DefaultActionConstraintProvider(),
            };

            var actionSelector = new ActionSelector(
                decisionTreeProvider,
                GetActionConstraintCache(actionConstraintProviders),
                NullLoggerFactory.Instance);

            var candidates = actionSelector.SelectCandidates(context);
            return (ControllerActionDescriptor)actionSelector.SelectBestCandidate(context, candidates);
        }

        private ControllerActionDescriptorProvider GetActionDescriptorProvider()
        {
            var controllerTypes = typeof(DefaultActionSelectorTests)
                .GetNestedTypes(BindingFlags.NonPublic)
                .Select(t => t.GetTypeInfo())
                .ToList();

            var options = new TestOptionsManager<MvcOptions>();

            var manager = GetApplicationManager(controllerTypes);

            var modelProvider = new DefaultApplicationModelProvider(options);

            var provider = new ControllerActionDescriptorProvider(
                manager,
                new[] { modelProvider },
                options);

            return provider;
        }

        private static ApplicationPartManager GetApplicationManager(List<TypeInfo> controllerTypes)
        {
            var manager = new ApplicationPartManager();
            manager.ApplicationParts.Add(new TestApplicationPart(controllerTypes));
            manager.FeatureProviders.Add(new TestFeatureProvider());
            return manager;
        }

        private static HttpContext GetHttpContext(string httpMethod)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = httpMethod;
            return httpContext;
        }

        private static ActionDescriptor[] GetActions()
        {
            return new ActionDescriptor[]
            {
                // Like a typical RPC controller
                CreateAction(area: null, controller: "Home", action: "Index"),
                CreateAction(area: null, controller: "Home", action: "Edit"),

                // Like a typical REST controller
                CreateAction(area: null, controller: "Product", action: null),
                CreateAction(area: null, controller: "Product", action: null),

                // RPC controller in an area with the same name as home
                CreateAction(area: "Admin", controller: "Home", action: "Index"),
                CreateAction(area: "Admin", controller: "Home", action: "Diagnostics"),
            };
        }

        private static IEnumerable<ActionDescriptor> GetActions(
            IEnumerable<ActionDescriptor> actions,
            string area,
            string controller,
            string action)
        {
            var comparer = new RouteValueEqualityComparer();

            return
                actions
                .Where(a => a.RouteValues.Any(kvp => kvp.Key == "area" && comparer.Equals(kvp.Value, area)))
                .Where(a => a.RouteValues.Any(kvp => kvp.Key == "controller" && comparer.Equals(kvp.Value, controller)))
                .Where(a => a.RouteValues.Any(kvp => kvp.Key == "action" && comparer.Equals(kvp.Value, action)));
        }

        private static ActionSelector CreateSelector(IReadOnlyList<ActionDescriptor> actions, ILoggerFactory loggerFactory = null)
        {
            loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            var actionProvider = new Mock<IActionDescriptorCollectionProvider>(MockBehavior.Strict);

            actionProvider
                .Setup(p => p.ActionDescriptors).Returns(new ActionDescriptorCollection(actions, 0));

            var decisionTreeProvider = new ActionSelectorDecisionTreeProvider(actionProvider.Object);

            var actionConstraintProviders = new IActionConstraintProvider[] {
                    new DefaultActionConstraintProvider(),
                    new BooleanConstraintProvider(),
                };

            return new ActionSelector(
                decisionTreeProvider,
                GetActionConstraintCache(actionConstraintProviders),
                loggerFactory);
        }

        private static VirtualPathContext CreateContext(object routeValues)
        {
            return CreateContext(routeValues, ambientValues: null);
        }

        private static VirtualPathContext CreateContext(object routeValues, object ambientValues)
        {
            return new VirtualPathContext(
                new Mock<HttpContext>(MockBehavior.Strict).Object,
                new RouteValueDictionary(ambientValues),
                new RouteValueDictionary(routeValues));
        }

        private static RouteContext CreateRouteContext(string httpMethod)
        {
            var routeData = new RouteData();
            routeData.Routers.Add(new Mock<IRouter>(MockBehavior.Strict).Object);

            var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var httpContext = new Mock<HttpContext>(MockBehavior.Strict);

            var request = new Mock<HttpRequest>(MockBehavior.Strict);
            request.SetupGet(r => r.Method).Returns(httpMethod);
            request.SetupGet(r => r.Path).Returns(new PathString());
            request.SetupGet(r => r.Headers).Returns(new HeaderDictionary());
            httpContext.SetupGet(c => c.Request).Returns(request.Object);
            httpContext.SetupGet(c => c.RequestServices).Returns(serviceProvider);

            return new RouteContext(httpContext.Object)
            {
                RouteData = routeData,
            };
        }

        private static ActionDescriptor CreateAction(string area, string controller, string action)
        {
            var actionDescriptor = new ControllerActionDescriptor()
            {
                ActionName = string.Format("Area: {0}, Controller: {1}, Action: {2}", area, controller, action),
                Parameters = new List<ParameterDescriptor>(),
            };

            actionDescriptor.RouteValues.Add("area", area);
            actionDescriptor.RouteValues.Add("controller", controller);
            actionDescriptor.RouteValues.Add("action", action);

            return actionDescriptor;
        }

        private static ActionConstraintCache GetActionConstraintCache(IActionConstraintProvider[] actionConstraintProviders = null)
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var descriptorProvider = new ActionDescriptorCollectionProvider(services);
            return new ActionConstraintCache(descriptorProvider, actionConstraintProviders.AsEnumerable() ?? new List<IActionConstraintProvider>());
        }

        private class BooleanConstraint : IActionConstraint
        {
            public bool Pass { get; set; }

            public int Order { get; set; }

            public bool Accept(ActionConstraintContext context)
            {
                return Pass;
            }
        }

        private class ConstraintFactory : IActionConstraintFactory
        {
            public IActionConstraint Constraint { get; set; }

            public bool IsReusable => true;

            public IActionConstraint CreateInstance(IServiceProvider services)
            {
                return Constraint;
            }
        }

        private class BooleanConstraintMarker : IActionConstraintMetadata
        {
            public bool Pass { get; set; }
        }

        private class BooleanConstraintProvider : IActionConstraintProvider
        {
            public int Order { get; set; }

            public void OnProvidersExecuting(ActionConstraintProviderContext context)
            {
                foreach (var item in context.Results)
                {
                    var marker = item.Metadata as BooleanConstraintMarker;
                    if (marker != null)
                    {
                        Assert.Null(item.Constraint);
                        item.Constraint = new BooleanConstraint() { Pass = marker.Pass };
                    }
                }
            }

            public void OnProvidersExecuted(ActionConstraintProviderContext context)
            {
            }
        }

        private class NonActionController
        {
            [NonAction]
            public void Put()
            {
            }

            [NonAction]
            public void RPCMethod()
            {
            }

            [NonAction]
            [HttpGet]
            public void RPCMethodWithHttpGet()
            {
            }
        }

        private class ActionNameController
        {
            [ActionName("CustomActionName_Verb")]
            public void Put()
            {
            }

            [ActionName("CustomActionName_DefaultMethod")]
            public void Index()
            {
            }

            [ActionName("CustomActionName_RpcMethod")]
            public void RPCMethodWithHttpGet()
            {
            }
        }

        private class HttpMethodAttributeTests_RestOnlyController
        {
            [HttpGet]
            [HttpPut]
            [HttpPost]
            [HttpDelete]
            [HttpPatch]
            [HttpHead]
            public void Put()
            {
            }

            [AcceptVerbs("PUT", "post", "GET", "delete", "pATcH")]
            public void Patch()
            {
            }
        }
    }
}
