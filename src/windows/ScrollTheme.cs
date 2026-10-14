using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace CodexAccountDesk;

internal static class ScrollTheme
{
    public static void Install(ResourceDictionary resources, Burn0Theme theme)
    {
        resources["DeskScrollNormal"] = theme.Color("muted");
        resources["DeskScrollHover"] = theme.Color("text");
        resources["DeskScrollPressed"] = theme.Color("accent");
        resources[SystemParameters.VerticalScrollBarWidthKey] = 12.0;
        resources[SystemParameters.HorizontalScrollBarHeightKey] = 12.0;
        resources[typeof(ScrollBar)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Style.Resources>
                <Style x:Key="PageButton" TargetType="RepeatButton">
                  <Setter Property="Focusable" Value="False"/>
                  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
                </Style>
                <Style x:Key="ScrollThumb" TargetType="Thumb">
                  <Setter Property="Background" Value="{DynamicResource DeskScrollNormal}"/>
                  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="3" Margin="3"/></ControlTemplate></Setter.Value></Setter>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource DeskScrollHover}"/></Trigger>
                    <Trigger Property="IsDragging" Value="True"><Setter Property="Background" Value="{DynamicResource DeskScrollPressed}"/></Trigger>
                  </Style.Triggers>
                </Style>
              </Style.Resources>
              <Setter Property="Width" Value="12"/><Setter Property="MinHeight" Value="24"/>
              <Setter Property="Background" Value="Transparent"/><Setter Property="Focusable" Value="False"/>
              <Setter Property="Template"><Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                  <Grid Background="Transparent">
                    <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True"
                           Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                           Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                           ViewportSize="{TemplateBinding ViewportSize}">
                      <Track.DecreaseRepeatButton><RepeatButton x:Name="Decrease" Command="{x:Static ScrollBar.PageUpCommand}" Style="{StaticResource PageButton}"/></Track.DecreaseRepeatButton>
                      <Track.Thumb><Thumb x:Name="Thumb" Style="{StaticResource ScrollThumb}" MinHeight="24"/></Track.Thumb>
                      <Track.IncreaseRepeatButton><RepeatButton x:Name="Increase" Command="{x:Static ScrollBar.PageDownCommand}" Style="{StaticResource PageButton}"/></Track.IncreaseRepeatButton>
                    </Track>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="Orientation" Value="Horizontal">
                      <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/>
                      <Setter TargetName="Thumb" Property="MinHeight" Value="0"/><Setter TargetName="Thumb" Property="MinWidth" Value="24"/>
                      <Setter TargetName="Decrease" Property="Command" Value="{x:Static ScrollBar.PageLeftCommand}"/>
                      <Setter TargetName="Increase" Property="Command" Value="{x:Static ScrollBar.PageRightCommand}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value></Setter>
              <Style.Triggers>
                <Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="12"/><Setter Property="MinHeight" Value="0"/><Setter Property="MinWidth" Value="24"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
              </Style.Triggers>
            </Style>
            """);
    }
}
