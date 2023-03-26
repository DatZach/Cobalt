using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiskUtil
{
    internal sealed class BTree<TEntry>
        where TEntry : IBTreeEntry, new()
    {
        public BTree(int degree)
        {
            if (degree < 2)
                throw new ArgumentException("BTree degree must be at least 2", "degree");

            Root = new Node(degree);
            Degree = degree;
            Height = 1;
        }

        public Node Root { get; private set; }

        public int Degree { get; private set; }

        public int Height { get; private set; }

        public uint LastNodeId { get; private set; }

        public TEntry AllocateNode()
        {
            var node = new TEntry { Key = LastNodeId++ };
            Insert(node);
            return node;
        }

        public TEntry Search(uint key)
        {
            return SearchInternal(Root, key);
        }
        
        public void Insert(TEntry newEntry)
        {
            // there is space in the root node
            if (!Root.HasReachedMaxEntries)
            {
                InsertNonFull(Root, newEntry);
                return;
            }

            // need to create new node and have it split
            var oldRoot = Root;
            Root = new Node(Degree);
            Root.Children.Add(oldRoot);
            SplitChild(Root, 0, oldRoot);
            InsertNonFull(Root, newEntry);

            Height++;
        }
        
        public void Delete(uint keyToDelete)
        {
            DeleteInternal(Root, keyToDelete);

            // if root's last entry was moved to a child node, remove it
            if (Root.Entries.Count == 0 && !Root.IsLeaf)
            {
                Root = Root.Children.Single();
                Height--;
            }
        }
        
        private void DeleteInternal(Node node, uint keyToDelete)
        {
            int i = node.Entries.TakeWhile(entry => keyToDelete.CompareTo(entry.Key) > 0).Count();

            // found key in node, so delete if from it
            if (i < node.Entries.Count && node.Entries[i].Key.CompareTo(keyToDelete) == 0)
            {
                DeleteKeyFromNode(node, keyToDelete, i);
                return;
            }

            // delete key from subtree
            if (!node.IsLeaf)
            {
                DeleteKeyFromSubtree(node, keyToDelete, i);
            }
        }

        private void DeleteKeyFromSubtree(Node parentNode, uint keyToDelete, int subtreeIndexInNode)
        {
            var childNode = parentNode.Children[subtreeIndexInNode];

            // node has reached min # of entries, and removing any from it will break the btree property,
            // so this block makes sure that the "child" has at least "degree" # of nodes by moving an 
            // entry from a sibling node or merging nodes
            if (childNode.HasReachedMinEntries)
            {
                int leftIndex = subtreeIndexInNode - 1;
                var leftSibling = subtreeIndexInNode > 0 ? parentNode.Children[leftIndex] : null;

                int rightIndex = subtreeIndexInNode + 1;
                var rightSibling = subtreeIndexInNode < parentNode.Children.Count - 1
                                                ? parentNode.Children[rightIndex]
                                                : null;
                
                if (leftSibling != null && leftSibling.Entries.Count > Degree - 1)
                {
                    // left sibling has a node to spare, so this moves one node from left sibling 
                    // into parent's node and one node from parent into this current node ("child")
                    childNode.Entries.Insert(0, parentNode.Entries[subtreeIndexInNode]);
                    parentNode.Entries[subtreeIndexInNode] = leftSibling.Entries.Last();
                    leftSibling.Entries.RemoveAt(leftSibling.Entries.Count - 1);

                    if (!leftSibling.IsLeaf)
                    {
                        childNode.Children.Insert(0, leftSibling.Children.Last());
                        leftSibling.Children.RemoveAt(leftSibling.Children.Count - 1);
                    }
                }
                else if (rightSibling != null && rightSibling.Entries.Count > Degree - 1)
                {
                    // right sibling has a node to spare, so this moves one node from right sibling 
                    // into parent's node and one node from parent into this current node ("child")
                    childNode.Entries.Add(parentNode.Entries[subtreeIndexInNode]);
                    parentNode.Entries[subtreeIndexInNode] = rightSibling.Entries.First();
                    rightSibling.Entries.RemoveAt(0);

                    if (!rightSibling.IsLeaf)
                    {
                        childNode.Children.Add(rightSibling.Children.First());
                        rightSibling.Children.RemoveAt(0);
                    }
                }
                else
                {
                    // this block merges either left or right sibling into the current node "child"
                    if (leftSibling != null)
                    {
                        childNode.Entries.Insert(0, parentNode.Entries[subtreeIndexInNode]);
                        var oldEntries = childNode.Entries;
                        childNode.Entries = leftSibling.Entries;
                        childNode.Entries.AddRange(oldEntries);
                        if (!leftSibling.IsLeaf)
                        {
                            var oldChildren = childNode.Children;
                            childNode.Children = leftSibling.Children;
                            childNode.Children.AddRange(oldChildren);
                        }

                        parentNode.Children.RemoveAt(leftIndex);
                        parentNode.Entries.RemoveAt(subtreeIndexInNode);
                    }
                    else
                    {
                        childNode.Entries.Add(parentNode.Entries[subtreeIndexInNode]);
                        childNode.Entries.AddRange(rightSibling.Entries);
                        if (!rightSibling.IsLeaf)
                        {
                            childNode.Children.AddRange(rightSibling.Children);
                        }

                        parentNode.Children.RemoveAt(rightIndex);
                        parentNode.Entries.RemoveAt(subtreeIndexInNode);
                    }
                }
            }

            // at this point, we know that "child" has at least "degree" nodes, so we can
            // move on - this guarantees that if any node needs to be removed from it to
            // guarantee BTree's property, we will be fine with that
            DeleteInternal(childNode, keyToDelete);
        }
        
        private void DeleteKeyFromNode(Node node, uint keyToDelete, int keyIndexInNode)
        {
            // if leaf, just remove it from the list of entries (we're guaranteed to have
            // at least "degree" # of entries, to BTree property is maintained
            if (node.IsLeaf)
            {
                node.Entries.RemoveAt(keyIndexInNode);
                return;
            }

            var predecessorChild = node.Children[keyIndexInNode];
            if (predecessorChild.Entries.Count >= Degree)
            {
                var predecessor = DeletePredecessor(predecessorChild);
                node.Entries[keyIndexInNode] = predecessor;
            }
            else
            {
                var successorChild = node.Children[keyIndexInNode + 1];
                if (successorChild.Entries.Count >= Degree)
                {
                    var successor = DeleteSuccessor(predecessorChild);
                    node.Entries[keyIndexInNode] = successor;
                }
                else
                {
                    predecessorChild.Entries.Add(node.Entries[keyIndexInNode]);
                    predecessorChild.Entries.AddRange(successorChild.Entries);
                    predecessorChild.Children.AddRange(successorChild.Children);

                    node.Entries.RemoveAt(keyIndexInNode);
                    node.Children.RemoveAt(keyIndexInNode + 1);

                    DeleteInternal(predecessorChild, keyToDelete);
                }
            }
        }
        
        private TEntry DeletePredecessor(Node node)
        {
            if (node.IsLeaf)
            {
                var result = node.Entries[^1];
                node.Entries.RemoveAt(node.Entries.Count - 1);
                return result;
            }

            return DeletePredecessor(node.Children.Last());
        }
        
        private TEntry DeleteSuccessor(Node node)
        {
            if (node.IsLeaf)
            {
                var result = node.Entries[0];
                node.Entries.RemoveAt(0);
                return result;
            }

            return DeletePredecessor(node.Children.First());
        }
        
        private TEntry SearchInternal(Node node, uint key)
        {
            int i = node.Entries.TakeWhile(entry => key.CompareTo(entry.Key) > 0).Count();

            if (i < node.Entries.Count && node.Entries[i].Key.CompareTo(key) == 0)
            {
                return node.Entries[i];
            }

            return node.IsLeaf ? default : SearchInternal(node.Children[i], key);
        }
        
        private void SplitChild(Node parentNode, int nodeToBeSplitIndex, Node nodeToBeSplit)
        {
            var newNode = new Node(Degree);

            parentNode.Entries.Insert(nodeToBeSplitIndex, nodeToBeSplit.Entries[Degree - 1]);
            parentNode.Children.Insert(nodeToBeSplitIndex + 1, newNode);

            newNode.Entries.AddRange(nodeToBeSplit.Entries.GetRange(Degree, Degree - 1));
            
            // remove also Entries[this.Degree - 1], which is the one to move up to the parent
            nodeToBeSplit.Entries.RemoveRange(Degree - 1, Degree);

            if (!nodeToBeSplit.IsLeaf)
            {
                newNode.Children.AddRange(nodeToBeSplit.Children.GetRange(Degree, Degree));
                nodeToBeSplit.Children.RemoveRange(Degree, Degree);
            }
        }

        private void InsertNonFull(Node node, TEntry newEntry)
        {
            int positionToInsert = node.Entries.TakeWhile(entry => newEntry.Key.CompareTo(entry.Key) >= 0).Count();

            // leaf node
            if (node.IsLeaf)
            {
                node.Entries.Insert(positionToInsert, newEntry);
                return;
            }

            // non-leaf
            var child = node.Children[positionToInsert];
            if (child.HasReachedMaxEntries)
            {
                SplitChild(node, positionToInsert, child);
                if (newEntry.Key.CompareTo(node.Entries[positionToInsert].Key) > 0)
                {
                    positionToInsert++;
                }
            }

            InsertNonFull(node.Children[positionToInsert], newEntry);
        }

        public void PrintTree()
        {
            PrintNode(Root, 0);
        }

        private void PrintNode(Node? node, int depth)
        {
            if (node == null)
                return;

            Console.Write(new string(' ', depth));
            for (var i = 0; i < node.Entries.Count; i++)
            {
                var item = node.Entries[i];
                Console.Write((item.ToString() ?? "(null)") + " ");
            }

            Console.WriteLine();

            if (!node.IsLeaf)
            {
                for (int i = 0; i < node.Children.Count; ++i)
                    PrintNode(node.Children[i], depth + 1);
            }
        }

        public void SerializeTree(Disk disk)
        {
            //writer.Write('T');
            //writer.Write('R');
            //writer.Write('E');
            //writer.Write('E');
            //writer.Write(Root.Cluster);
            //writer.Write(Degree);
            //writer.Write(0);

            SerializeNode(disk, Root);
        }

        private void SerializeNode(Disk disk, Node? node)
        {
            if (node == null)
                return;

            var sectorsPerCluster = 8; // HACK
            var sector = (uint)(node.Cluster * sectorsPerCluster + 1);
            var buffer = new byte[sectorsPerCluster * disk.BytesPerSector];
            using var stream = new MemoryStream(buffer);
            using var writer = new BinaryWriter(stream);

            for (int i = 0; i < (2 * Degree); ++i)
            {
                if (i >= node.Children.Count)
                {
                    writer.Write(-1);
                    continue;
                }

                writer.Write(node.Children[i].Cluster);
            }

            writer.BaseStream.Position += 128 - (2 * Degree) * 4;

            for (int i = 0; i < (2 * Degree) - 1; ++i)
            {
                if (i >= node.Entries.Count)
                {
                    writer.Write(-1);
                    writer.Write(new byte[124]);
                    continue;
                }

                var entry = node.Entries[i];
                entry.Serialize(writer);
            }

            writer.Flush();
            disk.Write(sector, sectorsPerCluster, buffer);

            for (int i = 0; i < node.Children.Count; ++i)
                SerializeNode(disk, node.Children[i]);
        }

        public void Deserialize(BinaryReader reader)
        {
            
        }

        private static Node DeserializeNode(uint clusterIndex, int degree, BinaryReader reader)
        {
            var node = new Node(degree);

            reader.BaseStream.Position = clusterIndex * 4096;
            
            reader.BaseStream.Position += 128 - 4;

            for (int i = 0; i < degree*2 - 1; ++i)
            {
                var value = new TEntry();
                value.Deserialize(reader);
                if (value.Key == 0xFFFFFFFF)
                    break;

                node.Entries.Add(value);
            }
            
            for (int i = 0; i < degree*2; ++i)
            {
                reader.BaseStream.Position = clusterIndex * 4096 + 4 + i * 4;
                var childCluster = reader.ReadUInt32();
                if (childCluster == 0xFFFFFFFF)
                    break;

                var child = DeserializeNode(childCluster, degree, reader);
                node.Children.Add(child);
            }

            return node;
        }

        public sealed class Node
        {
            private readonly int degree;

            public Node(int degree)
            {
                this.degree = degree;
                Children = new List<Node>(degree);
                Entries = new List<TEntry>(degree);
            }
            
            public uint Cluster { get; set; }

            public List<Node> Children { get; set; }

            public List<TEntry> Entries { get; set; }

            public bool IsLeaf => Children.Count == 0;

            public bool HasReachedMaxEntries => Entries.Count >= (2 * degree) - 1;

            public bool HasReachedMinEntries => Entries.Count == degree - 1;
        }
    }
    
    internal interface IBTreeEntry : IDiskSerializable
    {
        uint Key { get; set; }
    }
}
