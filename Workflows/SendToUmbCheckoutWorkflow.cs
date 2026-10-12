using UmbCheckout.Shared.Notifications;
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
        private readonly IStripeBasketPricingService _basketPricingService;

        public SendToUmbCheckoutWorkflow(ILogger<SendToUmbCheckoutWorkflow> logger, IHttpContextAccessor httpContextAccessor, IBasketService basketService, IStripeSessionService sessionService, IStripeBasketPricingService basketPricingService)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _basketService = basketService;
            _sessionService = sessionService;
            _basketPricingService = basketPricingService;
            this.Id = new Guid("c7371d3b-c6a6-4fc9-8a0c-7552932c13cb");
            this.Name = "Send form to UmbCheckout for payment";
            this.Description = "Sends the form to UmbCheckout, for payment to be carried out";
            this.Icon = "icon-price-pound";
            this.Group = "Payment";
        }


        [Setting("Email alias", Description = "The alias of the field used to capture the email address", DisplayOrder = 70, View = "TextField")]
        public string EmailAlias { get; set; } = "emailAddress";

        public override async Task<WorkflowExecutionStatus> ExecuteAsync(WorkflowExecutionContext context)
        {
            try
            {
                if (context == null)
                {
                    _logger.LogInformation("WorkflowExecutionContext Context is null");
                    _logger.LogCritical("WorkflowExecutionContext Context is null");
                    throw new Exception("Umbraco Context is missing... Why?");
                }

                // Checkout charges the stored basket: a site's Loaded hooks shape what shoppers see, never what is charged.
                Basket basket;
                using (UmbCheckoutHooks.SuppressLoaded())
                {
                    basket = await _basketService.Get();
                }

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
                // No basket page to return to here: Stripe's page shows the final amount.
                var repriced = (await _basketPricingService.RepriceAsync(basket)).Basket;
                var stripeSession = await _sessionService.CreateSessionAsync(repriced);
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
