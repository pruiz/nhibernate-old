using log4net;
using NHibernate.Collection;
using NHibernate.Engine;
using NHibernate.Persister.Collection;
using NHibernate.Persister.Entity;
using NHibernate.Type;

namespace NHibernate.Event.Default
{
	/// <summary> 
	/// Wrap collections in a Hibernate collection wrapper.
	/// </summary>
	public class WrapVisitor : ProxyVisitor
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(WrapVisitor));
		private bool substitute = false;

		internal WrapVisitor(IEventSource session) : base(session) { }

		internal bool SubstitutionRequired
		{
			get { return substitute; }
		}

		internal override void Process(object obj, IEntityPersister persister)
		{
			System.Object[] values = persister.GetPropertyValues(obj);
			IType[] types = persister.PropertyTypes;
			ProcessEntityPropertyValues(values, types);
			if (SubstitutionRequired)
			{
				persister.SetPropertyValues(obj, values);
			}
		}

		internal override object ProcessCollection(object collection, CollectionType collectionType)
		{
			IPersistentCollection coll = collection as IPersistentCollection;
			if (coll != null)
			{
				ISessionImplementor session = Session;
				if (coll.SetCurrentSession(session))
				{
					ReattachCollection(coll, coll.CollectionSnapshot);
				}
				return null;
			}
			else
			{
				return ProcessArrayOrNewCollection(collection, collectionType);
			}
		}

		private object ProcessArrayOrNewCollection(object collection, CollectionType collectionType)
		{
			if (collection == null)
			{
				//do nothing
				return null;
			}

			ISessionImplementor session = Session;

			ICollectionPersister persister = session.Factory.GetCollectionPersister(collectionType.Role);

			//TODO: move into collection type, so we can use polymorphism!

			if (collectionType.IsArrayType)
			{
				//if (collection == CollectionType.UNFETCHED_COLLECTION)
				//  return null;

				PersistentArrayHolder ah = session.GetCollectionHolder(collection);
				if (ah == null)
				{
					//ah = collectionType.Wrap(session, collection);
					ah = new PersistentArrayHolder(session, collection);
					session.AddNewCollection(persister, ah);
					session.AddCollectionHolder(ah);
				}
				return null;
			}
			else
			{
				IPersistentCollection persistentCollection = collectionType.Wrap(session, collection);
				session.AddNewCollection(persister, persistentCollection);

				if (log.IsDebugEnabled)
					log.Debug("Wrapped collection in role: " + collectionType.Role);

				return persistentCollection; //Force a substitution!
			}
		}

		internal override void ProcessValue(int i, object[] values, IType[] types)
		{
			System.Object result = ProcessValue(values[i], types[i]);
			if (result != null)
			{
				substitute = true;
				values[i] = result;
			}
		}

		internal override object ProcessComponent(object component, IAbstractComponentType componentType)
		{
			if (component != null)
			{
				object[] values = componentType.GetPropertyValues(component, Session);
				IType[] types = componentType.Subtypes;
				bool substituteComponent = false;
				for (int i = 0; i < types.Length; i++)
				{
					System.Object result = ProcessValue(values[i], types[i]);
					if (result != null)
					{
						values[i] = result;
						substituteComponent = true;
					}
				}
				if (substituteComponent)
				{
					componentType.SetPropertyValues(component, values);
				}
			}

			return null;
		}
	}
}