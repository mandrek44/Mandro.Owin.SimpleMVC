using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mandro.Owin.SimpleMVC
{
    public class Redirect
    {
        public static Uri To<T>(Expression<Func<T, Func<dynamic, dynamic>>> exprTree, object[] parameters = null)
        {
            var controllerType = exprTree.Parameters.First().Type;
            var method = (MethodInfo)((ConstantExpression)((MethodCallExpression)((UnaryExpression)exprTree.Body).Operand).Object).Value;

            return GetUrl(controllerType, method.Name, parameters);
        }

        private static Uri GetUrl(Type controllerType, string methodName, object[] parameters)
        {
            var parametersString = parameters != null ? string.Join("/", parameters.Select(property => property.ToString())) : string.Empty;
            if (!string.IsNullOrEmpty(parametersString)) parametersString = "/" + parametersString;

            methodName = GetMethodNameString(methodName);

            return new Uri("/" + controllerType.Name + "/" + methodName + parametersString, UriKind.Relative);
        }

        private static string GetMethodNameString(string methodName)
        {
            if (methodName.StartsWith("Get"))
            {
                return methodName.Substring("Get".Length);
            }
            if (methodName.StartsWith("Post"))
            {
                return methodName.Substring("Post".Length);
            }
            else
            {
                return methodName;
            }
        }
    }
}