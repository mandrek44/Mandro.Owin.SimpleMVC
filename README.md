Owin SimpleMVC
==============

Single MVC engine-middleware compatible with Owin.

## Example

 - Create new Console project
 - In `Package Manager Console`, type following:

       Install-Package Microsoft.Owin.SelfHost

 - Add reference to `Mandro.Owin.SimpleMVC` assembly
 - In Program.cs file, paste following content:

       using System;
       using Microsoft.Owin.Hosting;
       using Owin;

       namespace Mandro.Owin.SimpleMVC.Sample
       {
          class Program
          {
              static void Main(string[] args)
              {
                  using (WebApp.Start<Startup>("http://localhost:12345"))
                  {
                      Console.ReadLine();
                  }
              }
          }

          public class Startup
          {
              public void Configuration(IAppBuilder app)
              {
                  app.Use<SimpleMvcMiddleware>();
              }
          }
       }

 - In Solution Explorer, in project root, add Controllers folder. Add class `Home` to this folder:

       public class Home
       {
           public void GetIndex()
           {
           }
       }

 - In Solution Explorer, in project root, add Views/Home folder. Add file `GetIndex.cshtml` to this folder:

       <h1>Hello world!</h1>

 - Run and navigate to http://localhost:12345

 ## Documentation

 The library makes following assumptions:

  - The controller is any class that's directly in the `Controllers` folder, anywhere in the solution.
  - Controller is chosen using first part if Request path
     - If request is `/Account/Login` then the `Account` controller is selected.
     - If request is empty, `Home` controller is called by default.
   - The controllers have methods in format `<Verb><Name>`, i.e. `GetIndex`
     - `<Verb>` is request HTTP method written in Camel Case, i.e. `Get`, `Post`, `Put`, `Delete` etc.
     - `<Name>` is method name taken from second part of request Path.
     - If there's no second part in request path, then `Index` name is assumed.
   - The method can have one `dynamic` parameter
     - There'll always be `Context` field, initialized with current Owin Context
     - If request is `application/x-www-form-urlencoded` then form fields will be available through the parameter.
     - If there're extra parts in request path, then they will be added as `Param1`, `Param2`, etc...
   - By default, the corresponding View will be rendered
     - View will be searched in `Views\<Controller>\` folder. View file must match the name of controller method, i.e. `Views\Home\GetIndex.cshtml`.
   - When controller method returns `Uri`, browser will be redirected to that address
   - When controller method returns `byte[]`, the bytes will be written to resposne stream.
     - The controller method is responsible for setting proper headers, i.e. `Content-Type`
   - If controller or method is decorated with `Authorize` attribute, then user must first authenticate to call that controller/method. Sample controller login method:


    public dynamic PostLogin(dynamic loginDetails)
    {
        // Check following fields:
        // loginDetails.UserName
        // loginDetails.Password

        // If succeeded, then:
        var claims = new[] { new eClaim(ClaimTypes.Name, loginDetails.UserName) };
        var id = new ClaimsIdentity(claims, "Cookie");

        var context = loginDetails.Context.Authentication as IAuthenticationManager;
        context.SignIn(id);

        return Redirect.To((Home controller) => controller.GetIndex);
    }
