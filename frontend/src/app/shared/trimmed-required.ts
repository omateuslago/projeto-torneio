import { AbstractControl, ValidationErrors } from '@angular/forms';

export function trimmedRequired(control: AbstractControl<string>): ValidationErrors | null {
  return control.value?.trim() ? null : { required: true };
}
