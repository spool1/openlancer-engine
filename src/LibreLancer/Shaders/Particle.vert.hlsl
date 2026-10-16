#include "includes/Camera.hlsl"

// UBO: So must be aligned to 16
// 64 byte struct, can draw up to 256 per draw call/binding.
struct Particle
{
    float3 position;
    uint color;
    // XYZ: Normal, W: Rotation
    float3 normal;
    float rotate;
    // X: Left, Y: Top, Z: Right, W: Bottom
    float4 texCoords;
    //
    float2 halfSize; // XY + padding
    int motionBlurEnabled;
    float _pad1;
};

cbuffer ParticleParameters : register(b3, UNIFORM_SPACE)
{
    // 0 = basic
    // 1 = rect
    // 2 = perp
    int Type;
};

// The GL path flattens each block, and a flattened block has only one basic type. Thus the floats have their own block.
cbuffer ParticleStretch : register(b4, UNIFORM_SPACE)
{
    // The horizontal stretch of the drawn quads about the center of the view. 1 for an effect in the world.
    // A cutscene screen overlay is made for a 4:3 view, and it stretches to the width of a wider view.
    float ScreenStretch;
};

// Technically an SSBO, but we use UBOs to emulate this for GL3.0
StructuredBuffer<Particle> Particles : register(t9, TEXTURE_SPACE);

struct Output
{
    float2 texCoord : TEXCOORD0;
    float4 color : TEXCOORD1;
    float4 position : SV_Position;
};

float4 DiffuseToFloat4(uint inCol)
{
    float a = ((inCol & 0xff000000) >> 24);
    float b = ((inCol & 0xff0000) >> 16);
    float g = ((inCol & 0xff00) >> 8);
    float r = ((inCol & 0xff));
    return float4(r,g,b,a)/255.0;
}


Output main(int vertexID: SV_VertexID)
{
    const int index = vertexID / 6;
    const Particle particle = Particles[index];
    const int indices[6] = {0, 1, 2, 1, 3, 2};
    int vertex = indices[vertexID % 6];


    float3 p = particle.position;
    float4 color = DiffuseToFloat4(particle.color);

    float3 right;
    float3 up;
    float stretchVel;
    float3 toCamera;

    if (Type == 0)
    {
        if (!particle.motionBlurEnabled)
        {
            // Basic
            right = float3(
                View[0][0],
                View[1][0],
                View[2][0]
            );
            up = float3(
                View[0][1],
                View[1][1],
                View[2][1]
            );
        }
        else
        {
            //For motion blur the particle is aligned to the velocity vector (here passed via particle.normal) and stretched accordingly, while still being parallel to the screen.

            toCamera = normalize(CameraPosition - p);
            up=particle.normal;
            right=cross(up,toCamera);

            //TODO: There is probably a simpler and thus better solution instead of using the angle (e.g. projected vector onto screen plane / vector parallel to it)
            float angle = atan2(length(right),dot(up,toCamera));
            float velocity = length(particle.normal);
            stretchVel=velocity*0.03*abs(sin(angle)); //0.03: Chosen by visual inspection of the result

            //Build base vectors using Gram-Schmidt
            right=normalize(right);
            up = normalize(cross(toCamera,right));
            toCamera = normalize(cross(right, up));
        }
    }
    else if (Type == 1)
    {
        // Rect - aligned to the normal
        right = normalize(particle.normal);
        float3 toCamera = normalize(CameraPosition - p);
        up = cross(toCamera, right);
    }
    else if (Type == 2)
    {
        // Perp
        right = cross(particle.normal, float3(0.0, 1.0, 0.0));
        up = cross(right, particle.normal);
    }

    float3 positions[4];
    float3 vUp;
    float3 vRight;

    if (particle.motionBlurEnabled && (Type == 0))
    {
        //TODO: Optimize rendering. The way particles are rendered now makes this much more expensive (3 times the cost, since 3 results are not used).

        //Create rotation matrix for particle alignment to the velocity vector
        float3x3 rotmat=float3x3(right, up, toCamera);

        //For motion blur the start of the particle is being stretched to an earlier position depending on the velocity
        float3 quadPositions[4]={
            float3(-1.0 * particle.halfSize.x, -1.0 * particle.halfSize.y - stretchVel, 0.0),
            float3( 1.0 * particle.halfSize.x, -1.0 * particle.halfSize.y - stretchVel, 0.0),
            float3(-1.0 * particle.halfSize.x,  1.0 * particle.halfSize.y, 0.0),
            float3( 1.0 * particle.halfSize.x,  1.0 * particle.halfSize.y, 0.0),
        };

        //Apply rotation matrix
        positions[0] = p + mul(quadPositions[0], rotmat);
        positions[1] = p + mul(quadPositions[1], rotmat);
        positions[2] = p + mul(quadPositions[2], rotmat);
        positions[3] = p + mul(quadPositions[3], rotmat);
    }
    else
    {
        float s, c;
        sincos(particle.rotate, s, c);
        if (Type == 0)
        {
            // Basic: the texture is upright. U goes along the width (halfSize.x) and V goes down the height
            // (halfSize.y). The corners are in the order of the motion-blur branch and of the uvs table.
            float3 ax = (c * right - s * up) * particle.halfSize.x;
            float3 ay = (s * right + c * up) * particle.halfSize.y;
            positions[0] = p - ax - ay;
            positions[1] = p + ax - ay;
            positions[2] = p - ax + ay;
            positions[3] = p + ax + ay;
        }
        else
        {
            vUp = (c * right - s * up) * particle.halfSize.x;
            vRight = (s * right + c * up) * particle.halfSize.y;

            positions[0] = p - vRight - vUp;
            positions[1] = p + vRight - vUp,
            positions[2] = p - vRight + vUp,
            positions[3] = p + vRight + vUp;
        }
    }

    float2 uvs[4] = {
        particle.texCoords.xw,
        particle.texCoords.zw,
        particle.texCoords.xy,
        particle.texCoords.zy
    };

    Output output;
    output.position = mul(float4(positions[vertex], 1.0), ViewProjection);
    output.position.x *= ScreenStretch;
    output.texCoord = uvs[vertex];
    output.color = color;
    return output;
}
