/*
  Before including this shader, don�t forget to specify the number of splits
  and the size of shadow map.

  #define NUM_SPLITS 1
  #define SHADOW_MAP_SIZE 2048
*/

cbuffer cbShadowsBuffer {
	matrix gShadowViewProj[NUM_SPLITS];
}

Texture2D gShadowMaps[NUM_SPLITS];

#if ENABLE_SHADOWS != 1
float GetShadow(float3 position) {
	return 1.0;
}
#else

SamplerComparisonState samShadow {
	Filter = COMPARISON_MIN_MAG_MIP_LINEAR;
	AddressU = BORDER;
	AddressV = BORDER;
	AddressW = BORDER;
	BorderColor = float4(1.0f, 1.0f, 1.0f, 1.0f);
	ComparisonFunc = LESS;
};

#define _SHADOW_MAP_DX (1.0 / SHADOW_MAP_SIZE)

float GetShadowInner(Texture2D tex, float3 uv) {
	// uv: only float3 is required
	float shadow = 0.0, x, y;
	for (y = -1.5; y <= 1.5; y += 1.0)
		for (x = -1.5; x <= 1.5; x += 1.0)
			shadow += tex.SampleCmpLevelZero(samShadow, uv.xy + float2(x, y) * _SHADOW_MAP_DX, uv.z).r;
	// return shadow / 16.0;
	return saturate((shadow / 16 - 0.5) * 4 + 0.5);
}

#define _SHADOW_A 0.0001
#define _SHADOW_Z 0.9999

#if NUM_SPLITS == 1
float GetShadow(float3 position) {
	float4 uv = mul(float4(position, 1.0), gShadowViewProj[0]);
	uv.xyz /= uv.w;
	if (uv.x < _SHADOW_A || uv.x > _SHADOW_Z || uv.y < _SHADOW_A || uv.y > _SHADOW_Z)
		return 1;
	return GetShadowInner(gShadowMaps[0], uv.xyz);
}
#else
float GetShadow(float3 position) {
	float4 pos = float4(position, 1.0), uv, nv;

	uv = mul(pos, gShadowViewProj[NUM_SPLITS - 1]);
	uv.xyz /= uv.w;
	if (uv.x < _SHADOW_A || uv.x > _SHADOW_Z || uv.y < _SHADOW_A || uv.y > _SHADOW_Z)
		return 1;

	[flatten]
	for (int i = NUM_SPLITS - 1; i > 0; i--) {
		nv = mul(pos, gShadowViewProj[i - 1]);
		nv.xyz /= nv.w;
		if (nv.x < _SHADOW_A || nv.x > _SHADOW_Z || nv.y < _SHADOW_A || nv.y > _SHADOW_Z)
			return GetShadowInner(gShadowMaps[i], uv.xyz);
		uv = nv;
	}

	return GetShadowInner(gShadowMaps[0], nv.xyz);
}
#endif

#endif