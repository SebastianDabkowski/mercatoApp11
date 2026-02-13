using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SD.ProjectName.Modules.Products.Application
{
    public class ChangeProductWorkflowState
    {
        private readonly IProductRepository _repository;

        public ChangeProductWorkflowState(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task<WorkflowResult> SetStateAsync(ProductModel product, string targetState, bool isAdminOverride = false)
        {
            var errors = ValidateTransition(product, targetState, isAdminOverride);
            if (errors.Count > 0)
            {
                return WorkflowResult.Failed(errors);
            }

            product.WorkflowState = targetState;
            await _repository.Update(product);
            return WorkflowResult.Success();
        }

        private static List<string> ValidateTransition(ProductModel product, string targetState, bool isAdminOverride)
        {
            var errors = new List<string>();

            if (!ProductWorkflowStates.IsValid(targetState))
            {
                errors.Add("Invalid product state.");
                return errors;
            }

            if (product.WorkflowState == ProductWorkflowStates.Archived && !isAdminOverride)
            {
                errors.Add("Archived products cannot change state.");
                return errors;
            }

            if (!isAdminOverride && product.WorkflowState == ProductWorkflowStates.Active && targetState == ProductWorkflowStates.Draft)
            {
                errors.Add("Active products cannot be moved back to draft.");
            }

            if (!isAdminOverride && targetState == ProductWorkflowStates.Suspended && product.WorkflowState != ProductWorkflowStates.Active)
            {
                errors.Add("Only active products can be suspended.");
            }

            if (targetState == ProductWorkflowStates.Active || targetState == ProductWorkflowStates.Pending)
            {
                errors.AddRange(ValidateActivation(product));
            }

            return errors;
        }

        private static List<string> ValidateActivation(ProductModel product)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(product.Title))
            {
                errors.Add("Title is required to activate.");
            }

            if (string.IsNullOrWhiteSpace(product.Description))
            {
                errors.Add("Description is required to activate.");
            }

            if (string.IsNullOrWhiteSpace(product.Category) || !product.CategoryId.HasValue)
            {
                errors.Add("Category is required to activate.");
            }

            if (string.IsNullOrWhiteSpace(product.MainImageUrl))
            {
                errors.Add("At least one product image is required to activate.");
            }

            if (product.Price <= 0)
            {
                errors.Add("Price must be set to activate.");
            }

            if (product.Stock <= 0)
            {
                errors.Add("Stock must be greater than zero to activate.");
            }

            return errors;
        }
    }

    public record WorkflowResult(bool Succeeded, IReadOnlyList<string> Errors)
    {
        public static WorkflowResult Success() => new(true, Array.Empty<string>());

        public static WorkflowResult Failed(IEnumerable<string> errors) => new(false, errors.ToList());
    }
}
