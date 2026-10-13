using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UmbCheckout.Core.Interfaces;
using UmbCheckout.Shared.Models;
using UmbCheckout.Stripe.Interfaces;
using Umbraco.Cms.Web.Common.DependencyInjection;
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
        private readonly ISessionService _umbCheckoutSessionService;

        [Obsolete("Use the constructor that takes an IStripeBasketPricingService and an ISessionService. This one resolves them from the application's services.")]
        public SendToUmbCheckoutWorkflow(ILogger<SendToUmbCheckoutWorkflow> logger, IHttpContextAccessor httpContextAccessor, IBasketService basketService, IStripeSessionService sessionService)
            : this(logger, httpContextAccessor, basketService, sessionService, StaticServiceProvider.Instance.GetRequiredService<IStripeBasketPricingService>(), StaticServiceProvider.Instance.GetRequiredService<ISessionService>())
        {
        }

        [Obsolete("Use the constructor that also takes an ISessionService. This one resolves it from the application's services.")]
        public SendToUmbCheckoutWorkflow(ILogger<SendToUmbCheckoutWorkflow> logger, IHttpContextAccessor httpContextAccessor, IBasketService basketService, IStripeSessionService sessionService, IStripeBasketPricingService basketPricingService)
            : this(logger, httpContextAccessor, basketService, sessionService, basketPricingService, StaticServiceProvider.Instance.GetRequiredService<ISessionService>())
        {
        }

        [ActivatorUtilitiesConstructor]
        public SendToUmbCheckoutWorkflow(ILogger<SendToUmbCheckoutWorkflow> logger, IHttpContextAccessor httpContextAccessor, IBasketService basketService, IStripeSessionService sessionService, IStripeBasketPricingService basketPricingService, ISessionService umbCheckoutSessionService)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _basketService = basketService;
            _sessionService = sessionService;
            _basketPricingService = basketPricingService;
            _umbCheckoutSessionService = umbCheckoutSessionService;
            this.Id = new Guid("c7371d3b-c6a6-4fc9-8a0c-7552932c13cb");
            this.Name = "Send form to UmbCheckout for payment";
            this.Description = "Sends the form to UmbCheckout, for payment to be carried out";
            this.Icon = "icon-price-pound";
            this.Group = "Payment";
        }


        [Setting("Email alias", Description = "The alias of the field used to capture the email address", DisplayOrder = 70, View = "TextField")]
        public string EmailAlias { get; set; } = "emailAddress";

#if NET8_0_OR_GREATER
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

                // Umbraco Forms gives the workflow no token: the request's.
                var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
                var basket = await _basketService.Get(cancellationToken);
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
                // Checkout charges current published prices. There is no basket page to return to here: Stripe's page shows the final amount.
                var repriced = (await _basketPricingService.RepriceAsync(basket, cancellationToken)).Basket;
                if (!repriced.LineItems.Any())
                {
                    _logger.LogWarning("Form {FormId} was not sent to checkout: no product in the basket is still for sale", context.Form.Id);
                    return WorkflowExecutionStatus.Failed;
                }

                var stripeSession = await _sessionService.CreateSessionAsync(repriced, cancellationToken);

                // As StripeBasketController.Checkout: the success page clears this basket only when it records this checkout session,
                // which is recorded even if the customer has left meanwhile.
                repriced.CheckoutSessionId = stripeSession.Id;
                await _umbCheckoutSessionService.Update(repriced, CancellationToken.None);

                if (_httpContextAccessor.HttpContext != null)
                    _httpContextAccessor.HttpContext.Items[Constants.ItemKeys.RedirectAfterFormSubmitUrl] =
                        stripeSession.Url;
                return WorkflowExecutionStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Sending the form to checkout failed");
                return WorkflowExecutionStatus.Failed;
            }
        }
#else
        public override WorkflowExecutionStatus Execute(WorkflowExecutionContext context)
        {
            try
            {
                if (context == null)
                {
                    _logger.LogInformation("WorkflowExecutionContext Context is null");
                    _logger.LogCritical("WorkflowExecutionContext Context is null");
                    throw new Exception("Umbraco Context is missing... Why?");
                }

                // Umbraco Forms 10 has only this synchronous entry point, so the calls are waited for here. Forms gives no token: the request's.
                var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
                var basket = _basketService.Get(cancellationToken).GetAwaiter().GetResult();
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
                // Checkout charges current published prices. There is no basket page to return to here: Stripe's page shows the final amount.
                var repriced = _basketPricingService.RepriceAsync(basket, cancellationToken).GetAwaiter().GetResult().Basket;
                if (!repriced.LineItems.Any())
                {
                    _logger.LogWarning("Form {FormId} was not sent to checkout: no product in the basket is still for sale", context.Form.Id);
                    return WorkflowExecutionStatus.Failed;
                }

                var stripeSession = _sessionService.CreateSessionAsync(repriced, cancellationToken).GetAwaiter().GetResult();

                // As StripeBasketController.Checkout: the success page clears this basket only when it records this checkout session,
                // which is recorded even if the customer has left meanwhile.
                repriced.CheckoutSessionId = stripeSession.Id;
                _umbCheckoutSessionService.Update(repriced, CancellationToken.None).GetAwaiter().GetResult();

                if (_httpContextAccessor.HttpContext != null)
                    _httpContextAccessor.HttpContext.Items[Constants.ItemKeys.RedirectAfterFormSubmitUrl] =
                        stripeSession.Url;
                return WorkflowExecutionStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Sending the form to checkout failed");
                return WorkflowExecutionStatus.Failed;
            }
        }
#endif

        public override List<Exception> ValidateSettings()
        {
            List<Exception> exceptionList = new List<Exception>();
            if (string.IsNullOrWhiteSpace(this.EmailAlias))
                exceptionList.Add((Exception)new ArgumentNullException("Email alias", "'Email alias' setting has not been set'"));
            return exceptionList;
        }
    }
}
