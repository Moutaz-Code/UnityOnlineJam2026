# Lucky Number - proof of concept

Menu: **Lucky Number > Build POC Scene**, then press Play.
The builder creates `Assets/_Project/POC/LuckyNumberPOC.unity`, its materials, and puts the scene first in Build Settings.

## Controls
- WASD move, RMB drag or Q/E orbit the camera
- C: claim the face you stand on as your lucky number
- Hold Space to charge, release to jump. Space or LMB in the air: ground-pound
- L: toggle the "Lead core" upgrade prototype (pulls your face toward the sun)
- R: restart

## What it proves
- Walking on a d20 with gravity = -face normal, blended across edges (DiePlanet.BlendedLocalUp, PlanetWalker.edgeBlend)
- Slam impulse -> custom rigid body: w += (r x J) / I. No PhysX. Where you land decides the spin
- Settle: when slow, slerp the face nearest the sun onto the sun direction. That face is the result
- Player state lives in die-local space, so it rides the tumble with no parenting
- Run rules: land your number 3 times in 7 rolls

## Tuning knobs
- DiePlanet: inertia, angularDamping, settleSpeed, loadedStrength, numberCharacterSize
- PlanetWalker: slamForceMin/Max, forwardShare, chargeTime, edgeBlend
- PlanetCamera: distance, height, roll shot direction/distance

Throwaway code: IMGUI HUD and TextMesh numbers are placeholders for the real UI and number decals.
