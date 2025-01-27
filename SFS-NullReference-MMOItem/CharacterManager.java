package manager;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.LinkedList;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

import com.smartfoxserver.v2.SmartFoxServer;
import com.smartfoxserver.v2.api.ISFSMMOApi;
import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.mmo.IMMOItemVariable;
import com.smartfoxserver.v2.mmo.MMOItemVariable;
import com.smartfoxserver.v2.mmo.MMORoom;
import com.smartfoxserver.v2.mmo.Vec3D;

import data.Bullet;
import data.BulletState;
import data.Character;
import data.CharacterState;
import data.CharacterWeapon;
import data.Input;
import room.RoomExtension;

public class CharacterManager
{
    public static ConcurrentHashMap<User, Character> Characters;
    static ConcurrentLinkedQueue<Character> _characterQueue;

    static int _maxCharacters = 100;
    static ConcurrentLinkedQueue<Character> _characterPool;

    static RoomExtension _ext;
    static MMORoom _mmoRoom;
    static float _dt;

    static ScheduledFuture<?> _simulationTaskHandle;
    static ScheduledFuture<?> _sendPingsTaskHandle;

    static ISFSMMOApi _mmoAPI;

    public static void Init(RoomExtension ext, float dt)
    {
        _ext = ext;
        _mmoRoom = (MMORoom) _ext.getParentRoom();

        Characters = new ConcurrentHashMap<>();
        _characterQueue = new ConcurrentLinkedQueue<>();
        _characterPool = new ConcurrentLinkedQueue<>();

        for (int i = 0; i < _maxCharacters; i++)
        {
            _characterPool.add(new Character());
        }

        _bullets = BulletManager.GetBullets();

        _mmoAPI = SmartFoxServer.getInstance().getAPIManager().getMMOApi();

        _dt = dt;

        _simulationTaskHandle = SmartFoxServer.getInstance().getTaskScheduler()
                .scheduleAtFixedRate(new SimulationTask(), 0, (int) (_dt * 1000f), TimeUnit.MILLISECONDS);

        _sendPingsTaskHandle = SmartFoxServer.getInstance().getTaskScheduler().scheduleAtFixedRate(new SendPingsTask(),
                0, 4, TimeUnit.SECONDS);
    }

    public static void AddCharacter(User user)
    {
        var character = _characterPool.poll();

        character.SetUser(user);
        character.Transform.Position = new Vec3D(0f, 0f, 2f);

        character.Transform.Velocity = 8f;

        Characters.put(character.User, character);

        _characterQueue.add(character);
        InputManager.AddCharacter(character);
        _mmoAPI.setUserPosition(user, character.Transform.Position, user.getLastJoinedRoom());
    }

    public static void RemoveCharacter(User user)
    {
        var character = Characters.remove(user);

        _characterQueue.remove(character);
        InputManager.RemoveCharacter(character);
        _mmoRoom.removeUser(user);

        character.Reset();

        _characterPool.add(character);
    }

    static ArrayList<User> _processedUsers = new ArrayList<User>();
    static ArrayList<CharacterState> _processedStates = new ArrayList<CharacterState>();
    static ArrayList<Input> _processedInputs = new ArrayList<Input>();

    static ConcurrentLinkedQueue<Bullet> _bullets;

    static int _tick = 0;

    static boolean _replicateBulletProgress = false;
    static LinkedList<IMMOItemVariable> _variables = new LinkedList<>();
    static float _t;

    static class SimulationTask implements Runnable
    {
        @Override
        public void run()
        {
            try
            {
                _tick++;

                // Simulate character input
                for (Character character : _characterQueue)
                {
                    CharacterWeapon weapon = character.Weapon;

                    if (character.InputBuffer.Inputs.size() == 0)
                        continue;

                    // Movement
                    Input input = character.InputBuffer.Inputs.pollLast();
                    CharacterState state = MovementManager.ProcessInput(character, input);

                    // Attacking
                    if (input.AimAngle != -1)
                    {
                        long currentTime = System.currentTimeMillis();

                        if (currentTime - weapon.LastShotTime > weapon.CooldownMs)
                        {
                            long delayMs = currentTime - input.RequestTime;

                            weapon.LastShotTime = currentTime - delayMs - character.PingMs;

                            float executionDelay = delayMs / 1000.0f + character.Ping;
                            BulletManager.AddBullet(character, input.AimAngle, executionDelay);
                        }
                    }
                    InputManager.ReturnInputToPool(input);

                    _processedUsers.add(character.User);
                    _processedStates.add(state);
                }

                // Every 10th tick or abt .3333 seconds
                _replicateBulletProgress = (_tick % 10 == 0);

                Iterator<Bullet> iterator = _bullets.iterator();
                while (iterator.hasNext())
                {
                    Bullet bullet = iterator.next();

                    // Simulate bullet movement

                    bullet.AliveTime += _dt;

                    // Lerp the position by AliveTime
                    _t = bullet.AliveTime / bullet.MaxAliveTime;
                    bullet.Position = new Vec3D(bullet.Departure.x + _t * (bullet.Destination.x - bullet.Departure.x),
                            0f, bullet.Departure.z + _t * (bullet.Destination.z - bullet.Departure.z));

                    // Check for collision here

                    if (bullet.AliveTime >= bullet.MaxAliveTime)
                    {
                        _ext.trace("Removing bullet: " + bullet.ID);

                        iterator.remove();

                        _ext.trace("Removing bullet id:" + bullet.getId()); // not null, traces ID correctly
                        _mmoAPI.removeMMOItem(bullet); // nullException must happen here
                        _ext.trace("After MMOApi call"); // this never traces

                        BulletManager.ReturnToBulletPool(bullet);
                    } else
                    {
                        if (_replicateBulletProgress)
                        {
                            // Set the progress to be replicated to any client in range
                            _variables.clear();
                            var sfsObj = bullet.getVariable("d").getSFSObjectValue();
                            sfsObj.putFloat("a", bullet.AliveTime);
                            _variables.add(new MMOItemVariable("d", sfsObj));
                            _mmoAPI.setMMOItemVariables(bullet, _variables);
                        }

                        _mmoAPI.setMMOItemPosition(bullet, bullet.Position, _mmoRoom);
                    }
                }

                _ext.SendCharacterStates(_processedUsers, _processedStates);

                _processedUsers.clear();
                _processedStates.clear();
            }
            catch (Exception e)
            {
                // Can I log more details? This doesnt tell us much unfortunately as seen in
                // terminal
                _ext.trace("An error occurred in SimulationTask: " + e.getMessage());
                _ext.trace("Stack Trace: ", e);
            }
        }
    }

    static class SendPingsTask implements Runnable
    {
        @Override
        public void run()
        {
            for (Character character : _characterQueue)
            {
                _ext.PingCharacter(character);
                _ext.trace("Character " + character.User.getId() + " ping: " + character.PingMs);
            }
        }
    }
}
