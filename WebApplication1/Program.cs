using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WebApplication1
{ 
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }
         
        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }


    public class Startup
    {

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            var provider = services.BuildServiceProvider();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/", async context =>
                {

                    await context.Response.WriteAsync("Hello World!");
                });
            });
        }
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterType<PeopleAppService>().As<IPeopleAppService>().InstancePerLifetimeScope();

            var basePath = AppContext.BaseDirectory;

            var bllFilePath = Path.Combine(basePath, "WebApplication1.dll");

            List<Type> aops = new List<Type>();

            builder.RegisterType<LogAOP>();
            aops.Add(typeof(LogAOP));
            var webAssembly = Assembly.GetExecutingAssembly();

            var ass = Assembly.LoadFile(bllFilePath);
            //BLL layer registration service
            builder.RegisterAssemblyTypes(webAssembly)
                .AsImplementedInterfaces()
                .InstancePerDependency()
                .EnableInterfaceInterceptors()
                         .InterceptedBy(aops.ToArray())//register interceptor
                ;
        }
    }
    public interface IPeopleAppService
    {
        void Show();
        void Get(string param);
        Task<string> ShowTask();
        Task<string> GetTask(string param);
    }
    public class PeopleAppService : IPeopleAppService
    {
        public void Show()
        {
            Console.WriteLine("People");
        }
        public void Get(string param)
        {
            Console.WriteLine($"People Get {param}");
        }

        public async Task<string> ShowTask()
        {
            Console.WriteLine("People ShowTask");
            return await Task.Run(() => "People ShowTask");
        }
        public async Task<string> GetTask(string param)
        {
            return await Task.Run(() => $"People GetTask {param}");
        }
    }

    namespace QuickStart.Controllers
    {
        [ApiController]
        [Route("api")]
        public class DefaultController : ControllerBase
        {
            private readonly ILogger<DefaultController> _logger;
            private readonly IPeopleAppService _people;

            public DefaultController(ILogger<DefaultController> logger, IPeopleAppService people)
            {
                _people = people;
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            [HttpGet]
            public async Task<object> Get()
            {
                var responseObject = new
                {
                    Status = "Up"
                };
                _logger.LogInformation($"Status pinged: {responseObject.Status}");
                _people.Show();
                _people.Get("GET ...");
                await _people.ShowTask();
                await _people.GetTask("async");
                return responseObject;
            }
        }
    }

    public class LogAOP : IInterceptor
    {
        public LogAOP(ILogger<LogAOP> logger)
        {
            _logger = logger;
        }
        private readonly ILogger<LogAOP> _logger;
        public async void Intercept(IInvocation invocation)
        {
            var dataIntercept = "" +
               $"[Current Execution Method]: {invocation.Method.Name} \r\n" +
                               $"[The parameters carried are]: {JsonConvert.SerializeObject(invocation.Arguments)}\r\n";

            try
            {
                //Execute the current method   
                invocation.Proceed();

                var returnType = invocation.Method.ReturnType;
                //Asynchronous method
                if (IsAsyncMethod(invocation.Method))
                {

                    if (returnType != null && returnType == typeof(Task))
                    {
                        //Task returned by the waiting method
                        Func<Task> res = async () => await (Task)invocation.ReturnValue;

                        invocation.ReturnValue = res();
                    }
                    else //Task<TResult>
                    {
                        var returnType2 = invocation.Method.ReflectedType;//Get the return type

                        if (returnType2 != null)
                        {
                            var resultType = invocation.Method.ReturnType.GetGenericArguments()[0];

                            MethodInfo methodInfo = typeof(LogAOP).GetMethod("HandleAsync", BindingFlags.Instance | BindingFlags.Public);

                            var mi = methodInfo.MakeGenericMethod(resultType);
                            invocation.ReturnValue = mi.Invoke(this, new[] { invocation.ReturnValue });
                        }
                    }

                    var type = invocation.Method.ReturnType;
                    var resultProperty = type.GetProperty("Result");

                    if (resultProperty != null)
                        dataIntercept += ($"[execution completion result]: {JsonConvert.SerializeObject(resultProperty.GetValue(invocation.ReturnValue))}");
                }
                //Sync method
                else
                {
                    if (returnType != null && returnType == typeof(void))
                    {

                    }
                    else
                        dataIntercept += ($"[execution completion result]: {JsonConvert.SerializeObject(invocation.ReturnValue)}");
                }

                _logger.LogInformation(dataIntercept);

                await Task.Run(() =>
                {
                    Parallel.For(0, 1, e =>
                    {
                        LogHelper.LogInformation("AOPLog", dataIntercept);
                    });
                });
            }
            catch (Exception ex)
            {
                LogEx(ex, dataIntercept);
            }
        }

        //Construct an asynchronous method to wait for the return value
        public async Task<T> HandleAsync<T>(Task<T> task)
        {
            var t = await task;

            return t;
        }

        private void LogEx(Exception ex, string dataIntercept)
        {
            if (ex != null)
            {
                //In the executed service, catch the exception
                dataIntercept += ($"[Result of execution completion]: An exception occurred in the method: {ex.Message + ex.InnerException}\r\n");

                // There are detailed stack information in the exception log
                Parallel.For(0, 1, e =>
                {
                    LogHelper.LogInformation("AOPLog", dataIntercept);
                    _logger.LogWarning(dataIntercept);
                });
            }
        }

        /// <summary>
        /// Determine whether the asynchronous method
        /// </summary>
        public static bool IsAsyncMethod(MethodInfo method)
        {
            return (
                method.ReturnType == typeof(Task) ||
                (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                );
        }
    }


    public static class LogHelper
    {


        /// <summary>
        /// Exception log.
        /// </summary>
        /// <param name="ex">The excption need be logged.</param>
        /// <param name="severity">The severity of this exception, default value is <see cref="LogSeverity.Error" />.</param>
        public static void LogException(Exception ex)
        {
            var message = new StringBuilder(ex.Message).AppendLine().Append(ex.StackTrace).ToString();

            WriteLog("EXCEPTION:", message, LogLevel.Error);
        }

        /// <summary>
        ///  Serious error log.
        /// </summary>
        /// <param name="message">The fatal message.</param>
        public static void LogFatal(string category, string message)
        {
            WriteLog(category, message, LogLevel.Critical);
        }

        /// <summary>
        ///  General error log.
        /// </summary>
        /// <param name="message">The error message.</param>
        public static void LogError(string category, string message)
        {
            WriteLog(category, message, LogLevel.Error);
        }

        /// <summary>
        ///  Warning log.
        /// </summary>
        /// <param name="message">The warning message.</param>
        public static void LogWarning(string category, string message)
        {
            WriteLog(category, message, LogLevel.Warning);
        }

        /// <summary>
        ///  Information log.
        /// </summary>
        /// <param name="message">The information message.</param>
        public static void LogInformation(string category, string message)
        {
            WriteLog(category, message, LogLevel.Information);
        }

        /// <summary>
        ///  Debug logs.
        /// </summary>
        /// <param name="message">The debug message.</param>
        public static void LogDebug(string category, string message)
        {
            WriteLog(category, message, LogLevel.Debug);
        }

        /// <summary>
        ///  Write log information to the log file.
        /// </summary>
        /// <param name="message">Log information.</param>
        /// <param name="severity">Verification level.</param>
        private static void WriteLog(string category, string message, LogLevel severity)
        {
            Console.WriteLine(severity.ToString(), DateTime.UtcNow, message);
        }
    }


}
