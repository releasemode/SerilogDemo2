using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Enrichers.AspnetcoreHttpcontext;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.MSSqlServer;
using Serilog.Sinks.MSSqlServer.Sinks.MSSqlServer.Options;

namespace Simple.Serilog
{
    public static class SerilogHelpers
    {
        /// <summary>
        /// Provides standardized, centralized Serilog wire-up for a suite of applications.
        /// </summary>
        /// <param name="loggerConfig">Provide this value from the UseSerilog method param</param>
        /// <param name="applicationName">Represents the name of YOUR APPLICATION and will be used to segregate your app
        /// from others in the logging sink(s).</param>
        /// <param name="config">IConfiguration settings -- generally read this from appsettings.json</param>
        public static void WithSimpleConfiguration(this LoggerConfiguration loggerConfig, 
            string applicationName, IConfiguration config,IServiceProvider provider)
        {
            var name = Assembly.GetExecutingAssembly().GetName();

            loggerConfig
                .ReadFrom.Configuration(config) // minimum levels defined per project in json files 
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithAspnetcoreHttpcontext(provider,AddCustomContextInfo)
                .Enrich.WithProperty("Assembly", $"{name.Name}")
                .Enrich.WithProperty("Version", $"{name.Version}")
                    //.WriteTo.File(new CompactJsonFormatter(),
                    //    $@"C:\temp\Logs\{applicationName}.json");
                    //.WriteTo.Logger(lc => lc
                    //    .Filter.ByIncludingOnly(Matching.WithProperty("UsageName"))
                    .WriteTo.MSSqlServer(
                        connectionString: @"Server=DESKTOP-TS8641A;Database=LogingDB;User Id=sa;Password=123456",
                        sinkOptions: new SinkOptions { AutoCreateSqlTable = true, TableName = "UsageLog" },
                        columnOptions: GetSqlColumnOptions())
                .WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(Matching.WithProperty("UsageName")));
                    //    .WriteTo.Seq("http://localhost:5341"));
                    //.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri("http://localhost:9200"))
                    //    {
                    //        AutoRegisterTemplate = true,
                    //        AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv6,
                    //        IndexFormat = "log-{0:yyyy.MM.dd}"
                    //    }
                    //));
        }

        private static ColumnOptions GetSqlColumnOptions()
        {
            var options = new ColumnOptions();
            options.Store.Remove(StandardColumn.Message);
            options.Store.Remove(StandardColumn.MessageTemplate);
            //options.Store.Remove(StandardColumn.Level);
            //options.Store.Remove(StandardColumn.Exception);

            //options.Store.Remove(StandardColumn.Properties);
            options.Store.Add(StandardColumn.LogEvent);
            options.LogEvent.ExcludeStandardColumns = true;
            options.LogEvent.ExcludeAdditionalProperties = true;

            options.AdditionalColumns = new Collection<SqlColumn>
            {
                new SqlColumn
                {
                    ColumnName = "UsageName",
                    AllowNull = false,
                    DataType = SqlDbType.NVarChar,
                    DataLength = 200,
                    NonClusteredIndex = true

                },
                new SqlColumn
                {
                    ColumnName = "ActionName", AllowNull = false
                },
                new SqlColumn
                {
                    ColumnName = "MachineName", AllowNull = false
                },
                new SqlColumn
                {
                    ColumnName = "ClientIP", AllowNull = true
                },

            };

            return options;
        }

        public static void AddCustomContextInfo(IHttpContextAccessor ctx,
          LogEvent le, ILogEventPropertyFactory pf)
        {
            HttpContext context = ctx.HttpContext;
            if (context == null) return;

            //var userInfo = context.Items["my-custom-info"] as UserInfo;
            //if (userInfo == null)
            //{
            //    var user = context.User.Identity;
            //    if (user == null || !user.IsAuthenticated) return;
            //    var i = 0;
            //    userInfo = new UserInfo
            //    {
            //        Name = user.Name,
            //        Claims = context.User.Claims.ToDictionary(x => $"{x.Type} ({i++})", y => y.Value)
            //    };
            //    context.Items["my-custom-info"] = userInfo;
            //}

            var ClientIP = context.Connection.RemoteIpAddress;
            le.AddPropertyIfAbsent(pf.CreateProperty("ClientIP", ClientIP, false));

        }

        public static IApplicationBuilder UseSimpleSerilogRequestLogging(this IApplicationBuilder app)
        {
            return app.UseSerilogRequestLogging(opts =>
            {
                opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
                {
                    diagCtx.Set("ClientIP", httpCtx.Connection.RemoteIpAddress);
                    diagCtx.Set("UserAgent", httpCtx.Request.Headers["User-Agent"]);
                    LogContext.PushProperty("ClientIP", "10.12.2.3");
                  

                    if (httpCtx.User.Identity.IsAuthenticated)
                    {
                        var i = 0;
                        var userInfo = new UserInfo
                        {
                            Name = httpCtx.User.Identity.Name,
                            Claims = httpCtx.User.Claims.ToDictionary(x => $"{x.Type} ({i++})", y => y.Value)
                        };
                        diagCtx.Set("UserInfo", userInfo, true);
                    }
                };
            });
        }
    }
}
