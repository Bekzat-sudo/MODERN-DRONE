// Wind and blast-wave sway for VegetationLit (included in every pass after its vertex function).
// Globals from ModernDrone.Core.Wind / Shockwaves:
//   _WindVector (xyz wind at 10 m, m/s; w gustiness), _FxTime (game time, frozen with the world),
//   _Shocks[4] (xyz centre, w start time), _ShockParams[4] (x = strength, cube root of the charge; 0 = empty slot).
//   The slots are a ring buffer: all four are checked, the front's age discards old ones.
// The crown bends downwind with the square of the wind speed (drag) and the square of the height up the tree
// (a cantilevered trunk), plus a slow sway and, for alpha-clipped leaves, a fast flutter. A blast front reaching
// the tree at ~360 m/s shoves it away from the burst, then it swings back (ModernDrone.Core.Shockwaves.Push).
// Amplitudes are look values (abstract).
#ifndef MODERN_DRONE_VEGETATION_WIND
#define MODERN_DRONE_VEGETATION_WIND

float4 _WindVector;
float _FxTime;
float4 _Shocks[4];
float4 _ShockParams[4];

float3 VegetationDisplaceOS(float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
    // Height up the plant: trees are ~8-20 m; 12 m reference height.
    float h = saturate(max(positionWS.y - pivot.y, 0.0) / 12.0);
    float bend = h * h;
    float phase = dot(pivot.xz, float2(0.113, 0.071));

    float3 wind = _WindVector.xyz;
    float speed = length(wind);
    float3 dir = speed > 1e-3 ? wind / speed : float3(1, 0, 0);
    float sway = 0.012 * speed * speed * (0.75 + 0.25 * sin(_FxTime * 1.1 + phase)) + 0.04 * speed * sin(_FxTime * 1.9 + phase * 2.3) * _WindVector.w;
    float3 offset = dir * sway * bend;
    #if defined(_ALPHATEST_ON)
        // Leaves flutter.
        offset += 0.012 * (0.5 + speed * 0.2) * float3(sin(_FxTime * 7.3 + dot(positionWS, float3(2.1, 1.7, 2.9))),
                                                      sin(_FxTime * 6.1 + dot(positionWS, float3(1.3, 2.7, 1.9))),
                                                      sin(_FxTime * 8.2 + dot(positionWS, float3(2.7, 1.1, 2.3)))) * h;
    #endif

    [unroll] for (int i = 0; i < 4; i++)
    {
        if (_ShockParams[i].x <= 0.0) continue;
        float3 away = pivot - _Shocks[i].xyz;
        float d = length(away);
        float t = _FxTime - _Shocks[i].w - d / 360.0;
        if (t <= 0.0 || t > 1.6) continue;
        float strength = saturate(_ShockParams[i].x * 6.0 / max(d, 1.0));
        float push = strength * exp(-t * 3.0) * cos(t * 7.0) * saturate(t * 30.0);
        float3 horiz = float3(away.x, 0, away.z);
        horiz = dot(horiz, horiz) > 1e-4 ? normalize(horiz) : dir;
        offset += horiz * push * 2.2 * bend;
    }
    return TransformWorldToObject(positionWS + offset);
}

#endif
