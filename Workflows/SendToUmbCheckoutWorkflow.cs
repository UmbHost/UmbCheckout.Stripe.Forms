using UmbCheckout.Shared.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.DependencyInjection;
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
        private readonly ISessionService _umbCheckoutSessionService;

        [Obsolete("Use the constructor that takes an ISessionService. Scheduled for removal in the next major version.")]
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

        public override async Task<WorkflowExecutionStatus> ExecuteAsync(WorkflowExecutionContext context)
        {
            try
            {
                if (context == null)
                {
                    throw new InvalidOperationException("Umbraco Forms ran the workflow without a workflow execution context");
                }

                // Umbraco Forms passes no token: the submitting request's stops the work when the visitor leaves.
                var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

                // Checkout charges the stored basket: a site's Loaded hooks shape what shoppers see, never what is charged.
                Basket basket;
                using (UmbCheckoutHooks.SuppressLoaded())
                {
                    basket = await _basketService.Get(cancellationToken);
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
                var repriced = (await _basketPricingService.RepriceAsync(basket, cancellationToken)).Basket;
                var stripeSession = await _sessionService.CreateSessionAsync(repriced, cancellationToken);

                // As StripeBasketController.Checkout: the success page and the webhook clear this basket only when it
                // records this checkout session. Bookkeeping, not a shopper's change, so a site's Saving handlers
                // can't refuse it (or the paid basket would never clear).
                repriced.CheckoutSessionId = stripeSession.Id;
                using (UmbCheckoutHooks.SuppressSaving())
                {
                    await _umbCheckoutSessionService.Update(repriced, cancellationToken);
                }

                if (_httpContextAccessor.HttpContext != null)
                    _httpContextAccessor.HttpContext.Items[Constants.ItemKeys.RedirectAfterFormSubmitUrl] =
                        stripeSession.Url;
                return WorkflowExecutionStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending form {FormId} entry {RecordId} to Stripe checkout failed", context?.Form.Id, context?.Record.UniqueId);
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
