using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Autofac;

using Humanizer;

using Microsoft.Owin;

using RazorEngine;
using RazorEngine.Configuration;
using RazorEngine.Templating;

namespace Mandro.Owin.SimpleMVC
{
    public class SimpleMvcMiddleware : OwinMiddleware
    {
        private const string ViewFileExtension = ".cshtml";
        private const string ViewsFolderName = "Views";

        private Dictionary<string, Type> _controllersMap;

        private IContainer _container;

        public SimpleMvcMiddleware(OwinMiddleware next)
            : base(next)
        {
            InitializeContainer();
            LoadAppDomainControllers();

            Razor.SetTemplateService(new TemplateService(new TemplateServiceConfiguration
                                                         {
                                                             Resolver = new DelegateTemplateResolver(File.ReadAllText)
                                                         }));
        }

        public override async Task Invoke(IOwinContext context)
        {
            var query = await MvcQuery.ParseAsync(context.Request, _controllersMap);

            if (query == null || query.Controller == null)
            {
                await Next.Invoke(context);
                return;
            }

            if (!await TryAuthenticate(context, query))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            var methodResult = await TryRunControllerMethod(context, query);
            if (!methodResult.Success)
            {
                await Next.Invoke(context);
                return;
            }

            // Check if method returned something we can handle:
            // - Uri will cause redirection
            // - Byte array will cause immediate write to response
            if (methodResult.Result is Uri)
            {
                context.Response.Redirect((methodResult.Result as Uri).ToString());
                return;
            }
            else if (methodResult.Result is byte[])
            {
                await context.Response.WriteAsync(methodResult.Result as byte[]);
                return;
            }

            var templateContent = await ReadViewTemplate(query);
            var result = await Task.Run(() => Razor.Parse(templateContent, methodResult.Result));

            await context.Response.WriteAsync(result);
        }

        private async static Task<string> ReadViewTemplate(MvcQuery query)
        {
            var path = ViewsFolderName + "/" + query.Controller + "/" + query.Method + ViewFileExtension;
            using (var fileStream = File.OpenRead(path))
            using (var fileReader = new StreamReader(fileStream))
            {
                return await fileReader.ReadToEndAsync();
            }
        }

        private async Task<bool> TryAuthenticate(IOwinContext context, MvcQuery query)
        {
            var controllerType = _controllersMap[query.Controller];
            var controllerMethod = controllerType.GetMethods().FirstOrDefault(method => method.Name == query.Method);

            if (controllerType == null || controllerMethod == null)
            {
                return true;
            }

            if (NeedsAuthentication(controllerType) || NeedsAuthentication(controllerMethod))
            {
                var authenticateResult = await context.Authentication.AuthenticateAsync("Cookie");
                return authenticateResult != null && authenticateResult.Identity != null;
            }

            return true;
        }

        private static bool NeedsAuthentication(ICustomAttributeProvider controllerType)
        {
            return controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any();
        }

        private async Task<MethodResult> TryRunControllerMethod(IOwinContext context, MvcQuery query)
        {
            var controllerType = _controllersMap[query.Controller];
            var instance = _container.Resolve(controllerType);
            var controllerMethod = controllerType.GetMethods().FirstOrDefault(method => method.Name == query.Method);

            if (controllerMethod != null)
            {
                var methodParameterValues = GetMethodParameterValues(context, query.Parameters, controllerMethod);

                return await Task.Run(() => new MethodResult { Result = controllerMethod.Invoke(instance, methodParameterValues), Success = true });
            }
            else
            {
                return new MethodResult { Result = false };
            }
        }

        private static object[] GetMethodParameterValues(IOwinContext context, IDictionary<string, string> parameters, MethodInfo controllerMethod)
        {
            if (!controllerMethod.GetParameters().Any())
            {
                return new object[] { };
            }

            var expandoObject = new ExpandoObject();
            var dictionary = expandoObject as IDictionary<string, object>;

            foreach (var parameter in parameters)
            {
                dictionary.Add(parameter.Key.Dehumanize().Replace(" ", string.Empty), parameter.Value);
            }

            dictionary.Add("Context", context);

            return new object[] { expandoObject };
        }

        private void LoadAppDomainControllers()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            var controllers = assemblies.SelectMany(assembly => assembly.GetTypes().Where(type => type.Namespace != null && type.Namespace.EndsWith("Controllers")));
            _controllersMap = controllers.ToDictionary(keyItem => keyItem.Name, valueItem => valueItem);
        }

        private void InitializeContainer()
        {
            var containerBuilder = new ContainerBuilder();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                containerBuilder.RegisterAssemblyTypes(assembly).AsSelf().AsImplementedInterfaces();    
            }
            
            _container = containerBuilder.Build();
        }

        private class MethodResult
        {
            public bool Success { get; set; }

            public object Result { get; set; }
        }

        private class MvcQuery
        {
            private const string DefaultController = "Home";
            private const string DefaultControllerMethod = "Index";

            public static async Task<MvcQuery> ParseAsync(IOwinRequest request, IDictionary<string, Type> controllersMap)
            {
                return new MvcQuery
                       {
                           Controller = GetControllerName(request, controllersMap),
                           Method = GetMethodName(request, controllersMap),
                           Parameters = await GetParameters(request, controllersMap)
                       };
            }

            private static async Task<IDictionary<string, string>> GetParameters(IOwinRequest request, IDictionary<string, Type> controllersMap)
            {
                var httpMethod = request.Method.ToLower().Pascalize();
                var query = request.Path.Value;
                var queryParts = new Queue<string>(query.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries));

                if (!queryParts.Any())
                {
                    return new Dictionary<string, string>();
                }

                var controllerName = queryParts.Dequeue();
                if (!controllersMap.ContainsKey(controllerName))
                {
                    return new Dictionary<string, string>();
                }

                int paramIndex = 1;
                var parameters = new Dictionary<string, string>();
                if (request.ContentType == "application/x-www-form-urlencoded")
                {
                    var formCollection = await request.ReadFormAsync();
                    parameters = formCollection.ToDictionary(key => key.Key, value => value.Value.FirstOrDefault());
                }

                if (queryParts.Any())
                {
                    var potentialMethodName = queryParts.Dequeue();
                    if (controllersMap[controllerName].GetMethods().All(method => method.Name != httpMethod + potentialMethodName))
                    {
                        parameters.Add("Param" + paramIndex++, potentialMethodName);
                    }
                }

                while (queryParts.Any())
                {
                    parameters.Add("Param" + paramIndex++, queryParts.Dequeue());
                }

                return parameters;
            }

            private static string GetControllerName(IOwinRequest request, IDictionary<string, Type> controllersMap)
            {
                var query = request.Path.Value;

                if (query == "/")
                {
                    return DefaultController;
                }

                var controllerName = query.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!controllersMap.ContainsKey(controllerName))
                {
                    return null;
                }

                return controllerName;
            }

            private static string GetMethodName(IOwinRequest request, IDictionary<string, Type> controllersMap)
            {
                var httpMethod = request.Method.ToLower().Pascalize();
                var query = request.Path.Value;

                if (query == "/")
                {
                    return httpMethod + DefaultControllerMethod;
                }

                var queryParts = new Queue<string>(query.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries));
                if (!queryParts.Any())
                {
                    return null;
                }

                var controllerName = queryParts.Dequeue();
                if (!controllersMap.ContainsKey(controllerName))
                {
                    return null;
                }

                if (queryParts.Any())
                {
                    var potentialMethodName = queryParts.Dequeue();
                    if (controllersMap[controllerName].GetMethods().Any(method => method.Name == httpMethod + potentialMethodName))
                    {
                        return httpMethod + potentialMethodName;
                    }
                }

                return httpMethod + DefaultControllerMethod;
            }

            public IDictionary<string, string> Parameters { get; private set; }

            public string Method { get; private set; }

            public string Controller { get; private set; }
        }
    }
}