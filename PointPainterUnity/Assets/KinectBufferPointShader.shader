Shader "Unlit/KinectBufferPointShader"
{
	Properties
	{
        _PointSize("Far Point Size", Range(0, 0.01)) = .0045
		_CardUvScale("Card Uv Scale", Float) = .01
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{

			Cull Off
			CGPROGRAM
			#pragma vertex vert
			#pragma geometry geo
			#pragma fragment frag
			
			#include "UnityCG.cginc" 
            
			struct PointData
			{
				int depthVal;
				int R;
				int G;
				int B;
			};

            StructuredBuffer<float2> _DepthTable;
			StructuredBuffer<PointData> _PointsBuffer;

			struct v2g
			{
				float4 pos : SV_Position; 
                float2 uv : TEXCOORD0;
				float3 viewDir : TEXCOORD1;
                float cardSize : TEXCOORD2;
                float baseDepth : TEXCOORD3;
                float3 color : TEXCOORD4;
			};

			struct g2f
			{
				float4 vertex : SV_POSITION;
                float2 cardUv : TEXCOORD1;
                float3 color : TEXCOORD4;
			};

			uint _FramePointsCount;
            float _PointSize;
			float _CardUvScale;

            float4x4 _MasterTransform;
			
			fixed3 DecodeColorVal(int intR, int intG, int intB)
			{
				fixed r = (fixed)intR / 255;
				fixed g = (fixed)intG / 255;
				fixed b = (fixed)intB / 255;
				fixed3 ret = fixed3(r,g,b); // Gamma correct
				ret = pow(ret, 2.2);
				return ret;
			}
			
            v2g vert(uint meshId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
				PointData datum = _PointsBuffer[instanceId];
				float depthVal = (float)datum.depthVal / 1000; // Millimeters to meters
				
                float2 xyVal = _DepthTable[instanceId % _FramePointsCount] * depthVal;
                float3 basePos = float3(xyVal, depthVal);
				fixed3 colorVal = DecodeColorVal(datum.R, datum.G, datum.B);
				v2g o;
                o.color = colorVal;
				o.pos = mul(_MasterTransform, float4(basePos, 1));
                o.baseDepth = depthVal;
				o.viewDir = normalize(WorldSpaceViewDir(o.pos));
				return o;
			}

			[maxvertexcount(4)]
			void geo(point v2g p[1], inout TriangleStream<g2f> triStream)
			{
				float4 vertBase = p[0].pos;
				float4 vertBaseClip = UnityObjectToClipPos(vertBase);
				float size = _PointSize;

				// Calc vert points
				float4 leftScreenOffset = float4(size, 0, 0, 0);
				float4 rightScreenOffset = float4(-size, 0, 0, 0);
				float4 topScreenOffset = float4(0, -size, 0, 0);
				float4 bottomScreenOffset = float4(0, size, 0, 0); 

				float4 topVertA = leftScreenOffset + topScreenOffset + vertBaseClip;
				float4 topVertB = rightScreenOffset + topScreenOffset + vertBaseClip;
				float4 bottomVertA = leftScreenOffset + bottomScreenOffset + vertBaseClip;
				float4 bottomVertB = rightScreenOffset + bottomScreenOffset + vertBaseClip;

				g2f o;

                o.color = p[0].color;
				o.vertex = topVertB;
                o.cardUv = float2(0, 0);
				triStream.Append(o);

				o.vertex = topVertA;
                o.cardUv = float2(1, 0); 
				triStream.Append(o);

				o.vertex = bottomVertB;
                o.cardUv = float2(0, 1);
				triStream.Append(o);
                 
				o.vertex = bottomVertA;
                o.cardUv = float2(1, 1);
				triStream.Append(o);
			}
			
			fixed4 frag (g2f i) : SV_Target
			{
                return fixed4(i.color, 1);
			}
			ENDCG
		}
	}
}
