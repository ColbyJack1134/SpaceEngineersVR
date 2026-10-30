using System.Globalization;
using EmptyKeys.UserInterface;
using EmptyKeys.UserInterface.Controls;
using EmptyKeys.UserInterface.Input;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;

namespace SpaceEngineersVR.Player
{
    // Store, ATM, contract and trade screens are EmptyKeys views; their fields are not MyGuiControlTextbox.
    internal static class EmptyKeysText
    {
        internal static void TryOpen(MyGuiScreenBase screen)
        {
            if(!(screen is MyGuiScreenMvvmBase) || !(Hovered() is TextBox box) || !Editable(box)) return;
            MenuKeyboard.Open(box.Text,text=>Apply(box,text),()=>Editable(box),null,onSubmit:()=>Submit(box),maxLength:box.MaxLength>0 ? box.MaxLength:60);
        }

        private static bool Editable(TextBox box) => box.IsLoaded && box.IsEnabled && !box.IsReadOnly && box.Visibility==Visibility.Visible;

        // A NumericTextBox's +/- buttons are template children, so a button above the field means the click was not on the text.
        private static TextBox Hovered()
        {
            for(var element=InputManager.Current?.MouseDevice.MouseOverElement;element!=null;element=element.Parent ?? element.TemplatedParent as UIElement)
            {
                if(element is Button) return null;
                if(element is TextBox box) return box;
            }
            return null;
        }

        private static void Apply(TextBox box,string text)
        {
            if(!(box is NumericTextBox numeric)) box.Text=text;
            else if(float.TryParse(text,numeric.ValueStyle,CultureInfo.CurrentCulture,out float value)) numeric.Value=value;
        }

        // DONE does what Enter does in the field, such as the store search.
        private static void Submit(TextBox box)
        {
            foreach(var binding in box.InputBindings)
                if(binding.Gesture is KeyGesture gesture && gesture.Key==KeyCode.Enter && gesture.Modifiers==ModifierKeys.None &&
                    binding.Command?.CanExecute(binding.CommandParameter)==true)
                    binding.Command.Execute(binding.CommandParameter);
        }
    }

    // Vanilla multiplies the step only for gamepad shoulders; the VR pointer's other hand stands in for them.
    [HarmonyPatch(typeof(NumericTextBox),"ChangeValue")]
    internal static class NumericStepPatch
    {
        private static void Prefix(ref float change) => change*=MenuPointer.StepMultiplier;
    }
}
