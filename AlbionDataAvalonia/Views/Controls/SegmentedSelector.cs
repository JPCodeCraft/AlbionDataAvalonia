using Avalonia.Controls;
using System;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A compact single-choice selector with native selection and keyboard navigation. Items show their
/// <c>Label</c>; the look is in Themes/ControlThemes.axaml.
/// </summary>
public class SegmentedSelector : ListBox
{
    protected override Type StyleKeyOverride => typeof(SegmentedSelector);
}
