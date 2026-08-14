using System.Windows;
using GH_Net.WPF.ViewModels;
namespace GH_Net.WPF.Views;
public partial class TraceabilityWindow : Window { public TraceabilityWindow(TraceabilityViewModel vm) { InitializeComponent(); DataContext = vm; } }
