# Full Rumble controls

These are the default Player 1 bindings. Change them in **Settings > Input**. Keyboard, gamepad, and mouse
bindings can be remapped independently for Player 1 and Player 2. The controller-layout selector changes
button labels in the settings UI. **Reset to Defaults** applies the SDL standard mapping for a recognized
gamepad, or the generic raw-joystick map for an unrecognized joystick.

## Keyboard

| Game action | Key |
|---|---|
| Cross | Z |
| Circle | X |
| Square | A |
| Triangle | S |
| L1 | Q |
| R1 | W |
| L2 | E |
| R2 | R |
| L3 | F |
| R3 | G |
| Start | Enter |
| Select | Right Shift |
| Up | Up Arrow |
| Down | Down Arrow |
| Left | Left Arrow |
| Right | Right Arrow |

## Standard gamepads

SDL uses the connected controller's standard mapping. The face-button labels depend on the controller layout:

| Game action | Xbox | PlayStation | Nintendo Switch |
|---|---|---|---|
| Cross | A | Cross | B |
| Circle | B | Circle | A |
| Square | X | Square | Y |
| Triangle | Y | Triangle | X |
| L1 / R1 | LB / RB | L1 / R1 | L / R |
| L2 / R2 | LT / RT | L2 / R2 | ZL / ZR |
| L3 / R3 | Left / right stick click | L3 / R3 | Left / right stick click |
| Start | Menu | Options / Start | Plus |
| Select | View / Back | Create / Share | Minus |
| Directional input | D-pad | D-pad | D-pad |

The left and right analog sticks are passed through as analog axes. Trigger axes are used where the controller
exposes them. DualShock 3, DualSense, Xbox, and Switch controllers are supported through SDL when recognized;
the exact labels depend on the active SDL mapping and selected display layout.

## Generic USB gamepads

For an unrecognized USB joystick, connect it before opening Input settings, choose the **Generic** button-label
layout, then select **Reset to Defaults** on the Gamepad tab. The default raw joystick layout is:

| Game action | Generic joystick input |
|---|---|
| Cross / Circle / Square / Triangle | Buttons 1 / 2 / 3 / 4 |
| L1 / R1 | Buttons 5 / 6 |
| L2 / R2 | Buttons 7 / 8 |
| L3 / R3 | Buttons 9 / 10 |
| Start / Select | Buttons 11 / 12 |
| Up / Right / Down / Left | Hat 1 Up / Right / Down / Left |

The first four joystick axes are passed through as left-stick X/Y and right-stick X/Y. Extra axes are not bound
by default; bind them in the Gamepad tab if your controller uses them for triggers or other controls. The UI
labels axes and hats starting at 1; for example, Hat 1 is the joystick's first hat (SDL index 0). Controller
button numbering in this table starts at 1; the internal joystick button indexes start at 0.

If a device uses a different layout, bind buttons, axes, and hats manually in the Gamepad tab. You can bind
more than one input to an action.

## Mouse

Mouse bindings are unassigned by default. In **Settings > Input > Mouse**, click a binding and press the desired
mouse button or wheel direction. Horizontal mouse steering is optional and disabled by default.

## Player 2

Player 2 keyboard, gamepad, and mouse buttons are unassigned by default. Configure them separately in Input
settings. A connected second gamepad can be selected with the **Pad 2** tab.
