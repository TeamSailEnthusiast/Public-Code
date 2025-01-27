package data;

import com.smartfoxserver.v2.mmo.Vec3D;

import com.smartfoxserver.v2.mmo.MMOItem;
import com.smartfoxserver.v2.mmo.MMOItemVariable;

public class Bullet extends MMOItem
{
    public int ID;

    public Character Character;

    public float AliveTime;
    public Vector2D Departure = new Vector2D(0f, 0f);
    public Vector2D Destination = new Vector2D(0f, 0f);
    public Vector2D Direction;
    public float Velocity;

    public Vec3D Position;

    public float MaxAliveTime;

    public void Setup(Character character, Vec3D position, float aliveTime, float vel, short aimAngle)
    {
        Character = character;

        Departure.x = position.floatX();
        Departure.z = position.floatZ();
        Velocity = vel;

        MaxAliveTime = 10f;

        AliveTime = aliveTime;

        // Convert short degrees to float with a decimal by dividing by 10.
        float angle = aimAngle / 10f;
        double angleRadians = angle * 0.017453292f; // Convert to radians
        Direction = new Vector2D((float) Math.sin(angleRadians), (float) Math.cos(angleRadians));

        Destination.x = position.floatX() + Direction.x * MaxAliveTime * Velocity;
        Destination.z = position.floatZ() + Direction.z * MaxAliveTime * Velocity;

        // Lerp the position by AliveTime
        float t = AliveTime / MaxAliveTime;
        Position = new Vec3D(Departure.x + t * (Destination.x - Departure.x), 0f,
                Departure.z + t * (Destination.z - Departure.z));

        // Add networked variables for client-replication
        var sfsObj = getVariable("d").getSFSObjectValue();
        sfsObj.putByte("t", (byte) 0);

        sfsObj.putFloat("x", Departure.x);
        sfsObj.putFloat("z", Departure.z);
        sfsObj.putFloat("l", Destination.x);
        sfsObj.putFloat("u", Destination.z);

        sfsObj.putFloat("a", AliveTime);
        sfsObj.putFloat("m", MaxAliveTime);

        setVariable(new MMOItemVariable("d", sfsObj)); // Add the sfsObj as an MMOItem variable
    }
}
