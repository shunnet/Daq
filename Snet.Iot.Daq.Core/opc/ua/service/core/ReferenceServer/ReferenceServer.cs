/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Snet.Iot.Daq.Core.opc.ua.service.core.DurableSubscription;
using Snet.Model.data;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static Snet.Iot.Daq.Core.opc.core.Data;

namespace Snet.Iot.Daq.Core.opc.ua.service.core.ReferenceServer
{
    /// <summary>
    /// Implements the Quickstart Reference Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// 
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the EmptyNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    public partial class ReferenceServer : ReverseConnectServer
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="User">用户名</param>
        /// <param name="Password">密码</param>
        /// <param name="AType">验证类型</param>
        /// <param name="AutoCreateAddress">自动创建地址</param>
        /// <param name="AddressSpaceName">地址空间名称</param>
        public ReferenceServer(string User, string Password, AuType AType, bool AutoCreateAddress, string AddressSpaceName, Action<object?, EventDataResult> actionEvent)
        {

            this.ActionEvent = actionEvent;
            this.User = User;
            this.Password = Password;
            this.AType = AType;
            this.AutoCreateAddress = AutoCreateAddress;
            this.AddressSpaceName = AddressSpaceName;
        }

        public override Task<WriteResponse> WriteAsync(SecureChannelContext secureChannelContext, RequestHeader requestHeader, WriteValueCollection nodesToWrite, CancellationToken ct)
        {
            ActionEvent?.Invoke(this, new EventDataResult(true, "客户端写入操作", nodesToWrite));
            return base.WriteAsync(secureChannelContext, requestHeader, nodesToWrite, ct);
        }


        #region Properties
        public ITokenValidator TokenValidator { get; set; }


        /// <summary>
        /// 客户端写入事件抛出
        /// </summary>
        private Action<object?, EventDataResult> ActionEvent;

        /// <summary>
        /// 账号密码
        /// </summary>
        private string User, Password;
        /// <summary>
        /// 认证类型
        /// </summary>
        private AuType AType;

        /// <summary>
        /// 地址管理
        /// </summary>
        public ReferenceNodeManager NodeManage;
        /// <summary>
        /// 自动创建地址
        /// </summary>
        public bool AutoCreateAddress;
        /// <summary>
        /// 地址空间名称
        /// </summary>
        public string AddressSpaceName;

        #endregion

        #region Overridden Methods
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            m_logger.LogInformation(
                Utils.TraceMasks.StartStop,
                "Creating the Reference Server Node Manager.");

            //如果地址管理为空则实例化
            if (NodeManage == null)
            {
                NodeManage = new ReferenceNodeManager(server, configuration, AutoCreateAddress, AddressSpaceName);
            }

            IList<INodeManager> nodeManagers = new List<INodeManager>();
            nodeManagers.Add(NodeManage);
            foreach (INodeManagerFactory nodeManagerFactory in NodeManagerFactories)
            {
                nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
            }
            return new MasterNodeManager(server, configuration, null, null, nodeManagers);
        }

        protected override IMonitoredItemQueueFactory CreateMonitoredItemQueueFactory(IServerInternal server, ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.DurableSubscriptionsEnabled == true)
            {
                return new DurableMonitoredItemQueueFactory(server.Telemetry);
            }
            return new MonitoredItemQueueFactory(server.Telemetry);
        }

        /// <summary>
        /// Creates the subscriptionStore for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a subscriptionStore for a server, the return type is <seealso cref="ISubscriptionStore"/>.</returns>
        protected override ISubscriptionStore CreateSubscriptionStore(IServerInternal server, ApplicationConfiguration configuration)
        {
            if (configuration?.ServerConfiguration?.DurableSubscriptionsEnabled == true)
            {
                return new SubscriptionStore(server);
            }
            return null;
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            return new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart Reference Server",
                ProductUri = "http://opcfoundation.org/Quickstart/ReferenceServer/v1.04",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var resourceManager = new ResourceManager(configuration);

            foreach (
                System.Reflection.FieldInfo field in typeof(StatusCodes).GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                uint? id = field.GetValue(typeof(StatusCodes)) as uint?;

                if (id != null)
                {
                    resourceManager.Add(id.Value, "en-US", field.Name);
                }
            }

            return resourceManager;
        }

        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the 
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            m_logger.LogInformation(Utils.TraceMasks.StartStop, "The server is starting.");

            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validator for X509 identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            // request notifications when the user identity is changed. all valid users are accepted by default.
            server.SessionManager.ImpersonateUser
                += new ImpersonateEventHandler(SessionManager_ImpersonateUser);

            try
            {
                ServerInternal.UpdateServerStatus(
                    status =>
                        // allow a faster sampling interval for CurrentTime node.
                        status.Variable.CurrentTime.MinimumSamplingInterval = 250);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Override some of the default user token policies for some endpoints.
        /// </summary>
        /// <remarks>
        /// Sample to show how to override default user token policies.
        /// </remarks>
        public override UserTokenPolicyCollection GetUserTokenPolicies(ApplicationConfiguration configuration, EndpointDescription description)
        {
            UserTokenPolicyCollection policies = base.GetUserTokenPolicies(
                configuration,
                description);

            // sample how to modify default user token policies
            if (description.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.Certificate)];
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.Sign)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.Anonymous)];
            }
            else if (description.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep &&
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                return [.. policies.Where(u => u.TokenType != UserTokenType.UserName)];
            }
            return policies;
        }
        #endregion

        #region User Validation Functions
        /// <summary>
        /// Creates the objects used to validate the user identity tokens supported by the server.
        /// </summary>
        private void CreateUserIdentityValidators(ApplicationConfiguration configuration)
        {
            for (int ii = 0; ii < configuration.ServerConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = configuration.ServerConfiguration.UserTokenPolicies[ii];

                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // check if user certificate trust lists are specified in configuration.
                    if (configuration.SecurityConfiguration.TrustedUserCertificates != null &&
                        configuration.SecurityConfiguration.UserIssuerCertificates != null)
                    {
                        var certificateValidator = new CertificateValidator(MessageContext.Telemetry);
                        certificateValidator.UpdateAsync(configuration.SecurityConfiguration)
                            .Wait();
                        certificateValidator.Update(
                            configuration.SecurityConfiguration.UserIssuerCertificates,
                            configuration.SecurityConfiguration.TrustedUserCertificates,
                            configuration.SecurityConfiguration.RejectedCertificateStore);

                        // set custom validator for user certificates.
                        m_userCertificateValidator = certificateValidator.GetChannelValidator();
                    }
                }
            }
        }

        /// <summary>
        /// 当客户端试图更改身份时调用
        /// </summary>
        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            switch (AType)
            {
                case AuType.Anonymous:
                    //匿名
                    if (args.NewIdentity is AnonymousIdentityToken)
                    {
                        args.Identity = new RoleBasedIdentity(new UserIdentity(), new List<Role>() { Role.Anonymous });
                        return;
                    }
                    break;
                case AuType.UserName:
                    // 账号密码
                    UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
                    if (userNameToken != null)
                    {
                        args.Identity = VerifyPassword(userNameToken);
                        return;
                    }
                    break;
                case AuType.Certificate:
                    //证书
                    X509IdentityToken x509Token = args.NewIdentity as X509IdentityToken;
                    if (x509Token != null)
                    {
                        VerifyUserTokenCertificate(x509Token.Certificate);
                        args.Identity = new RoleBasedIdentity(new UserIdentity(x509Token), new List<Role>() { Role.AuthenticatedUser });
                        return;
                    }
                    break;
            }
            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "不支持用户令牌类型: {0}.", args.NewIdentity);
        }

        /// <summary>
        /// 验证证书
        /// </summary>
        /// <param name="certificate"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifyUserTokenCertificate(X509Certificate2 certificate)
        {
            try
            {
                if (m_userCertificateValidator != null)
                {
                    m_userCertificateValidator.ValidateAsync(certificate, default).GetAwaiter().GetResult();
                }
                else
                {
                    CertificateValidator.ValidateAsync(certificate, default).GetAwaiter().GetResult();
                }
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                ServiceResultException se = e as ServiceResultException;
                if (se != null && se.StatusCode == StatusCodes.BadCertificateUseNotAllowed)
                {
                    info = new TranslationInfo(
                        "InvalidCertificate",
                        "en-US",
                        "'{0}' 是无效的用户证书",
                        certificate.Subject);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' 不是受信任用户证书",
                        certificate.Subject);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(result.Code, info.Key),
                        new LocalizedText(info)));
            }
        }

        /// <summary>
        /// 验证账号密码
        /// </summary>
        private IUserIdentity VerifyPassword(UserNameIdentityToken userNameToken)
        {
            string userName = userNameToken.UserName;
            byte[] password = userNameToken.DecryptedPassword;
            if (string.IsNullOrEmpty(userName))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid,
                    "安全令牌不是有效的用户名令牌。不接受空的用户名");
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected,
                    "安全令牌不是有效的用户名令牌。不接受空密码");
            }
            if (userName != User || !Utils.IsEqual(password, Encoding.UTF8.GetBytes(Password)))
            {
                // 使用默认文本构造翻译对象。
                TranslationInfo info = new TranslationInfo(
                    "InvalidPassword",
                    "en-US",
                    "无效的用户名或密码",
                    userName);

                // 使用供应商定义的子代码创建异常。
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(StatusCodes.BadUserAccessDenied, "账号或密码错误"),
                        new LocalizedText(info)));
            }
            return new RoleBasedIdentity(
                new UserIdentity(userNameToken),
                [Role.AuthenticatedUser]);
        }

        #endregion

        #region Private Fields
        private ICertificateValidator m_userCertificateValidator;
        #endregion
    }
}
