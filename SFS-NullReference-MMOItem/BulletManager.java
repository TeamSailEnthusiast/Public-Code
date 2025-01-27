package manager;

import java.util.concurrent.ConcurrentLinkedQueue;

import com.smartfoxserver.v2.SmartFoxServer;
import com.smartfoxserver.v2.api.ISFSMMOApi;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.mmo.MMOItemVariable;
import com.smartfoxserver.v2.mmo.MMORoom;

import data.Bullet;
import data.Character;
import room.RoomExtension;

public class BulletManager
{
    static RoomExtension _ext;
    static float _dt;

    static ConcurrentLinkedQueue<Bullet> _bullets = new ConcurrentLinkedQueue<>();

    static int _maxBullets = 5;
    static ConcurrentLinkedQueue<Bullet> _bulletPool;

    static MMORoom _room;
    static ISFSMMOApi _mmoAPI;

    public static void Init(RoomExtension ext, float dt)
    {
        _ext = ext;
        _dt = dt;

        _room = (MMORoom) _ext.getParentRoom();
        _mmoAPI = SmartFoxServer.getInstance().getAPIManager().getMMOApi();

        _bulletPool = new ConcurrentLinkedQueue<>();
        for (int i = 0; i < _maxBullets; i++)
        {
            var bullet = new Bullet();
            var sfsObj = new SFSObject();
            sfsObj.putByte("t", (byte) 0);

            sfsObj.putFloat("x", 0f);
            sfsObj.putFloat("z", 0f);
            sfsObj.putFloat("l", 0f);
            sfsObj.putFloat("u", 0f);

            sfsObj.putFloat("a", 0f);
            sfsObj.putFloat("m", 1f);

            bullet.setVariable(new MMOItemVariable("d", sfsObj)); // Add the sfsObj as an MMOItem variable

            bullet.ID = bullet.getId();

            _bulletPool.add(bullet);
        }
    }

    public static void AddBullet(Character character, short aimAngle, float executionDelay)
    {
        var position = character.Transform.Position;

        var bullet = _bulletPool.poll();

        if (bullet == null)
        {
            _ext.trace("Bullet from pool is null!"); // This never happens
        }

        float aliveTime = character.Ping + executionDelay; // = Math.max(value, 0.05f);

        bullet.Setup(character, position, aliveTime, 1f, aimAngle);

        float alivTime = bullet.getVariable("d").getSFSObjectValue().getFloat("a");
        _ext.trace("bulletPool size after spawning bullet: " + _bulletPool.size());

        _bullets.add(bullet); // Add to the global bullet queue

        _mmoAPI.setMMOItemPosition(bullet, bullet.Position, _room);
    }

    public static void ReturnToBulletPool(Bullet bullet)
    {
        _bullets.remove(bullet);
        _bulletPool.add(bullet);

        _ext.trace("bulletPool size after despawning bullet: " + _bulletPool.size());
    }

    // static ArrayList<BulletState> _bulletStates = new ArrayList<BulletState>();
    // static ArrayList<List<User>> _proxyLists = new ArrayList<List<User>>();

    // static ArrayList<Bullet> _removingBullets = new ArrayList<Bullet>();

    // static class ProcessBulletsTask implements Runnable
    // {
    // @Override
    // public void run()
    // {
    // for (Bullet bullet : _bullets)
    // {
    // bullet.Position = new Vec3D(bullet.Position.floatX() + bullet.Direction.x *
    // bullet.Velocity * _dt, 0f,
    // bullet.Position.floatZ() + bullet.Direction.z * bullet.Velocity * _dt);
    // bullet.AliveTime += _dt;

    // // Check for collision here

    // if (bullet.AliveTime >= bullet.MaxAliveTime)
    // {
    // _removingBullets.add(bullet);
    // continue;
    // }

    // _mmoAPI.setMMOItemPosition(bullet, bullet.Position, _room);

    // _bulletStates.add(new BulletState(bullet.ID, bullet.Position.floatX(),
    // bullet.Position.floatZ()));
    // _proxyLists.add(_room.getProximityList(bullet.Position)); // Get a list of
    // users in proximity of the
    // // bullet
    // }
    // _ext.SendBulletStates(_bulletStates, _proxyLists); // send bullet to all
    // users in proximity that can see it

    // for (Bullet bullet : _removingBullets)
    // {
    // _bullets.remove(bullet);
    // _mmoAPI.removeMMOItem(bullet);
    // _bulletPool.add(bullet);
    // }
    // _removingBullets.clear();

    // _bulletStates.clear();
    // _proxyLists.clear();
    // }
    // }

    public static ConcurrentLinkedQueue<Bullet> GetBullets()
    {
        return _bullets;
    }
}
