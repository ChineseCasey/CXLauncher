"""Measure the opaque theme colors and every gradient endpoint used behind text."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
document = ET.parse(root / "src/CXLauncher/Themes/Dark.xaml").getroot()
keys = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
resources = {element.attrib.get(keys): element for element in document}
def colors(key):
    node = resources[key]
    return [node.attrib["Color"]] if "Color" in node.attrib else [child.attrib["Color"] for child in node if "Color" in child.attrib]
def opaque(hex_color):
    if len(hex_color) == 9:
        alpha = int(hex_color[1:3],16)/255
        return '#'+''.join(f'{round(int(hex_color[index:index+2],16)*alpha+255*(1-alpha)):02X}' for index in (3,5,7))
    return hex_color

def luminance(hex_color):
    hex_color = opaque(hex_color)
    channels = [int(hex_color[index:index+2], 16)/255 for index in (1,3,5)]
    linear = [channel/12.92 if channel <= .04045 else ((channel+.055)/1.055)**2.4 for channel in channels]
    return sum(channel*weight for channel,weight in zip(linear,(.2126,.7152,.0722)))
def contrast(left, right):
    a,b=sorted((luminance(left),luminance(right)))
    return (b+.05)/(a+.05)

def overlay(hex_color, alpha):
    hex_color = opaque(hex_color)
    channels = [round(int(hex_color[index:index+2],16)*(1-alpha)+255*alpha) for index in (1,3,5)]
    return '#'+''.join(f'{channel:02X}' for channel in channels)
checks = []
for foreground,background,threshold in [
    ("TextBrush","BackgroundBrush",4.5),
    ("TextBrush","FrameSurfaceBrush",4.5),
    ("MutedBrush","FrameSurfaceBrush",4.5),
    ("MutedBrush","HoverBrush",4.5),
    ("OnAccentBrush","PrimarySurfaceBrush",4.5),
    ("OnAccentBrush","PrimaryHoverBrush",4.5),
    ("ErrorBrush","FrameSurfaceBrush",4.5),
    ("ControlBorderBrush","SurfaceBrush",3),
]:
    ratio = min(contrast(left,right) for left in colors(foreground) for right in colors(background))
    checks.append({"foreground":foreground,"background":background,"minimumRatio":round(ratio,2),"required":threshold,"passed":ratio>=threshold})
for foreground in ("TextBrush", "MutedBrush"):
    ratio = min(contrast(left,overlay(right,24/255)) for left in colors(foreground) for right in colors("FrameSurfaceBrush"))
    checks.append({"foreground":foreground,"background":"FrameSurfaceBrush + peak glass sheen","minimumRatio":round(ratio,2),"required":4.5,"passed":ratio>=4.5})
if not all(check["passed"] for check in checks):
    raise ValueError(json.dumps(checks))
(Path(__file__).resolve().parent / "contrast-verification.json").write_text(json.dumps(checks,indent=2), encoding="utf-8")
print(json.dumps(checks,indent=2))
