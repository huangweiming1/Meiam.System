using Autofac;
using Autofac.Extras.DynamicProxy;
using MapsterMapper;
using Meiam.System.Common;
using Meiam.System.Core;
using Meiam.System.Hostd.Authorization;
using Meiam.System.Hostd.Common;
using Meiam.System.Hostd.Extensions;
using Meiam.System.Hostd.Global;
using Meiam.System.Hostd.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace Meiam.System.Hostd
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Env = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Env { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            #region ��������
            services.AddCors(c =>
            {
                c.AddPolicy("LimitRequests", policy =>
                {
                    policy
                    .WithOrigins(AppSettings.Configuration["Startup:CorsIPs"].Split(';'))
                    .AllowAnyHeader()//Ensures that the policy allows any header.
                    .AllowAnyMethod();
                });
            });
            #endregion

            #region �Զ�ӳ��
            services.AddScoped<IMapper, ServiceMapper>();
            #endregion

            #region Api�ĵ�˵��
            services.AddSwaggerGen(c =>
            {

                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = $"{AppSettings.Configuration["Startup:ApiName"]} �ӿ��ĵ�",
                    Description = $"{AppSettings.Configuration["Startup:ApiName"]} HTTP API "
                });

                try
                {
                    //��������
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, "Meiam.System.Hostd.xml");//������Ǹո����õ�xml�ļ���
                    c.IncludeXmlComments(xmlPath, true);//Ĭ�ϵĵڶ���������false�������controller��ע�ͣ��ǵ��޸�

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Xml �ļ���ʧ�����鲢������\n{ ex.Message}");
                }

                // ������ȨС��
                c.OperationFilter<AppendAuthorizeFilter>();

            });
            #endregion

            #region ����Json��ʽ
            services.AddMvc().AddNewtonsoftJson(options =>
            {
                // ����ѭ������
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                // ��ʹ���շ�
                //options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                // ����ʱ���ʽ
                options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
                // ���ֶ�Ϊnullֵ�����ֶβ��᷵�ص�ǰ��
                //options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            });
            #endregion

            #region ��ȡ�ͻ��� IP
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
            #endregion

            #region ������

            //ע�뻺��
            services.AddMemoryCache();

            //ע�� HTTPCONTEXT
            services.AddHttpContextAccessor();

            //ע�� TokenManager
            services.AddScoped<TokenManager>();

            //ע��ȫ���쳣����
            services.AddControllers(options =>
            {
                //ȫ���쳣����
                options.Filters.Add<GlobalExceptions>();
                //ȫ����־
                options.Filters.Add<GlobalActionMonitor>();

            })
            .ConfigureApiBehaviorOptions(options =>
            {
                //����ϵͳ�Դ�ģ����֤
                options.SuppressModelStateInvalidFilter = true;  
            });

            //�����ƻ�����
            services.AddTaskSchedulers();

            //ע��REDIS ����
            RedisServer.Initalize();

            #endregion
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            #region ����������ʾ

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            };
            #endregion

            #region ��������
            app.UseCors("LimitRequests");
            #endregion

            #region Api�ĵ�˵��
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                var ApiName = AppSettings.Configuration["Startup:ApiName"];

                c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
                c.RoutePrefix = ""; //·�����ã�����Ϊ�գ���ʾֱ���ڸ�������localhost:8001�����ʸ��ļ�,ע��localhost:8001/swagger�Ƿ��ʲ����ģ�ȥlaunchSettings.json��launchUrlȥ����������뻻һ��·����ֱ��д���ּ��ɣ�����ֱ��дc.RoutePrefix = "doc";
            });
            #endregion

            #region ������

            // ������־���
            app.UseMiddleware<RequestMiddleware>();
            // ʹ�þ�̬�ļ�
            app.UseForwardedHeaders();
            // ʹ�þ�̬�ļ�
            app.UseStaticFiles();
            // ʹ��cookie
            app.UseCookiePolicy();
            // ʹ��Routing
            app.UseRouting();

            app.UseResponseCaching();

            // �ָ�����
            app.UseAddTaskSchedulers();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            #endregion
        }

        #region �Զ�ע�����
        public void ConfigureContainer(ContainerBuilder builder)
        {
            var assemblysServices = Assembly.Load("Meiam.System.Interfaces");
            builder.RegisterAssemblyTypes(assemblysServices)
                .InstancePerDependency()//˲ʱ����
               .AsImplementedInterfaces()////�Զ�����ʵ�ֵ����нӿ����ͱ�¶������IDisposable�ӿڣ�
               .EnableInterfaceInterceptors(); //����Autofac.Extras.DynamicProxy;
        }
        #endregion
    }
}