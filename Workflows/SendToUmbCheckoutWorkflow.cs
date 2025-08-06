using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UmbCheckout.Core.Interfaces;
using UmbCheckout.Shared.Models;
using UmbCheckout.Stripe.Interfaces;
using Umbraco.Forms.Core;
using Umbraco.Forms.Core.Attributes;
using Umbraco.Forms.Core.Enums;

namespace UmbCheckout.Stripe.Forms.Workflows
{
    public class SendToUmbCheckoutWorkflow : WorkflowType
    {
        private readonly ILogger<SendToUmbCheckoutWorkflow> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IBasketService _basketService;
        private readonly IStripeSessionService _sessionService;

        public SendToUmbCheckoutWorkflow(ILogger<SendToUmbCheckoutWorkflow> logger, IHttpContextAccessor httpContextAccessor, IBasketService basketService, IStripeSessionService sessionService)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _basketService = basketService;
            _sessionService = sessionService;
            this.Id = new Guid("c7371d3b-c6a6-4fc9-8a0c-7552932c13cb");
            this.Name = "Send form to UmbCheckout for payment";
            this.Description = "Sends the form to UmbCheckout, for payment to be carried out";
            this.Icon = "icon-price-pound";
            this.Group = "Payment";
        }


        [Setting("Email alias", Description = "The alias of the field used to capture the email address", DisplayOrder = 70, View = "TextField")]
        public string EmailAlias { get; set; } = "emailAddress";
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task<WorkflowExecutionStatus> ExecuteAsync(WorkflowExecutionContext context)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                if (context == null)
                {
                    _logger.LogInformation("WorkflowExecutionContext Context is null");
                    _logger.LogCritical("WorkflowExecutionContext Context is null");
                    throw new Exception("Umbraco Context is missing... Why?");
                }

                var basket = _basketService.Get().Result;
                basket.CustomerReferenceId = $"{context.Form.Id}@{context.Record.UniqueId}";

                var emailAddress = context.Record.GetRecordFieldByAlias(EmailAlias);
                if (emailAddress != null && emailAddress.HasValue())
                {
                    var emailValue = context.Record.GetRecordFieldByAlias(EmailAlias)?.ValuesAsString();
                    if (!string.IsNullOrEmpty(emailValue))
                    {
                        basket.Customer = new Customer
                        {
                            EmailAddress = emailValue
                        };
                    }
                }
                var stripeSession = _sessionService.CreateSessionAsync(basket).Result;
                if (_httpContextAccessor.HttpContext != null)
                    _httpContextAccessor.HttpContext.Items[Constants.ItemKeys.RedirectAfterFormSubmitUrl] =
                        stripeSession.Url;
                return WorkflowExecutionStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
                return WorkflowExecutionStatus.Failed;
            }
        }

        public override List<Exception> ValidateSettings()
        {
            List<Exception> exceptionList = new List<Exception>();
            if (string.IsNullOrWhiteSpace(this.EmailAlias))
                exceptionList.Add((Exception)new ArgumentNullException("Email alias", "'Email alias' setting has not been set'"));
            return exceptionList;
        }
    }
}
